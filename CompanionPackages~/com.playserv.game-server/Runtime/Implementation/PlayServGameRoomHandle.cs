using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Owns one logical room registration and its single-flight heartbeat loop.</summary>
    public sealed class PlayServGameRoomHandle : IDisposable
    {
        private readonly object _sync = new object();
        private readonly PlayServGameServerContext _context;
        private readonly string _registryKey;
        private readonly Action<string, PlayServGameRoomHandle> _remove;
        private readonly SemaphoreSlim _heartbeatGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _loopCancellation = new CancellationTokenSource();

        private PlayServGameRoomSnapshot _desiredSnapshot;
        private PlayServRoomPlacementAcknowledgment _placement;
        private PlayServGameRoomState _state;
        private DateTimeOffset? _lastHeartbeatAt;
        private PlayServError _lastError = PlayServError.None;
        private Task _loopTask;
        private Task<PlayServGameRoomCloseResult> _closeTask;
        private readonly HashSet<string> _roster = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _admissionIds = new Dictionary<string, string>(StringComparer.Ordinal);
        internal object AdmissionLock => _sync;
        internal bool CanAdmit => _state == PlayServGameRoomState.Active || _state == PlayServGameRoomState.Degraded;
        /// <summary>Local pushed-ticket admission. Subscribe to AdmissionRejected before accepting game connections.</summary>
        public PlayServRoomAdmission Admission { get; }
        private PlayServRoomConnectionTracker _connectionTracker;
        /// <summary>Creates this room's opt-in pushed-admission tracker. Connection IDs must identify a unique connection generation.</summary>
        public PlayServRoomConnectionTracker CreateConnectionTracker(TimeSpan? reconnectGrace = null)
        {
            lock (_sync)
            {
                ThrowIfClosedOrTerminated();
                if (_connectionTracker != null) throw new InvalidOperationException("A connection tracker already exists for this room.");
                if (PlayServGameServer.Uplink.AdmissionMode != PlayServAdmissionMode.Push)
                    throw UplinkErrors.Exception("admission_mode_mismatch");
                return _connectionTracker = new PlayServRoomConnectionTracker(this, _context, reconnectGrace ?? TimeSpan.FromSeconds(30));
            }
        }
        private Task _presenceTask = Task.CompletedTask;
        private int _queuedPresence, _mismatches;
        private bool _divergenceReported;
        private PlayServRoomConfiguration _configuration;
        private readonly double _startedAt;
        private double _emptySince;

        internal PlayServGameRoomHandle(
            PlayServGameServerContext context,
            string functionSlug,
            PlayServGameRoomSnapshot desiredSnapshot,
            string registryKey,
            Action<string, PlayServGameRoomHandle> remove)
        {
            _context = context;
            FunctionSlug = functionSlug;
            _desiredSnapshot = desiredSnapshot;
            _registryKey = registryKey;
            _remove = remove;
            Admission = new PlayServRoomAdmission(this);
            _state = PlayServGameRoomState.Active;
            _startedAt = _emptySince = context.MonotonicSeconds();
        }

        /// <summary>Raised after a heartbeat failure has updated <see cref="State"/>.</summary>
        public event Action<PlayServGameRoomHandle, PlayServError> HeartbeatFailed;
        /// <summary>Raised when the backend placement acknowledgment changes.</summary>
        public event Action<PlayServGameRoomHandle, PlayServRoomPlacementAcknowledgment> PlacementChanged;
        /// <summary>Raised once after a non-retryable heartbeat failure terminates the handle.</summary>
        public event Action<PlayServGameRoomHandle, PlayServError> Terminated;
        /// <summary>Raised once per unresolved roster-repair episode, after two subsequent mismatches.</summary>
        public event Action<PlayServGameRoomHandle> PresenceDiverged;
        /// <summary>Raised after a platform lifetime/idle timer closes the room.</summary>
        public event Action<PlayServGameRoomHandle, string> LifetimeClosed;
        public PlayServRoomConfiguration Configuration { get { lock (_sync) return _configuration; } }

        /// <summary>Reports a game-admitted player. Call only for non-bot players, after the game's admission decision.</summary>
        public void ReportJoin(string playerId, bool isBot = false)
        {
            if (isBot) return;
            var push = PlayServGameServer.Uplink.AdmissionMode == PlayServAdmissionMode.Push;
            playerId = PlayServGameServer.ValidateOptionalAnalyticsPlayerId(playerId);
            if (playerId == null) throw new ArgumentException("A player ID is required.", nameof(playerId));
            lock (_sync)
            {
                ThrowIfClosedOrTerminated();
                if (!_context.EnableUplink) throw new InvalidOperationException("Presence requires the server uplink.");
                if (_roster.Contains(playerId)) return;
                if (push) throw UplinkErrors.Exception("admission_mode_mismatch");
                _roster.Add(playerId);
                EnqueuePresence(new { type = "room_presence", room_name = RoomName, @event = "join",
                    seq = _context.NextPresenceSequence(RoomName), player_id = playerId });
            }
        }

        /// <summary>Reports the game's removal decision, not a raw transport disconnect or reconnect grace period.</summary>
        public void ReportLeave(string playerId)
        {
            playerId = PlayServGameServer.ValidateOptionalAnalyticsPlayerId(playerId);
            if (playerId == null) throw new ArgumentException("A player ID is required.", nameof(playerId));
            lock (_sync)
            {
                ThrowIfClosedOrTerminated();
                if (!_context.EnableUplink) throw new InvalidOperationException("Presence requires the server uplink.");
                if (!_roster.Remove(playerId)) return;
                _admissionIds.Remove(playerId);
                if (_roster.Count == 0) _emptySince = _context.MonotonicSeconds();
                EnqueuePresence(new { type = "room_presence", room_name = RoomName, @event = "leave",
                    seq = _context.NextPresenceSequence(RoomName), player_id = playerId });
            }
        }
        /// <summary>Reports a kick already decided/enforced by the game. Does not control the game's network transport.</summary>
        public void Kick(string playerId) => ReportLeave(playerId);

        // Called in room -> admission lock order. No callbacks are delivered under either lock.
        internal bool AddAdmittedPlayer(string player, string admissionId, string replacingAdmissionId = null)
        {
            lock (_sync)
            {
                if (!CanAdmit || _queuedPresence >= 256) return false;
                if (_roster.Contains(player))
                {
                    if (replacingAdmissionId == null || !_admissionIds.TryGetValue(player, out var current) || current != replacingAdmissionId) return false;
                }
                else _roster.Add(player);
                _admissionIds[player] = admissionId;
                return true;
            }
        }
        internal bool RemoveAdmittedPlayer(string player, string admissionId)
        {
            lock (_sync)
            {
                if (!_admissionIds.TryGetValue(player, out var current) || current != admissionId) return false;
                _admissionIds.Remove(player); _roster.Remove(player);
                if (_roster.Count == 0) _emptySince = _context.MonotonicSeconds();
                if (CanAdmit) EnqueuePresence(new { type = "room_presence", room_name = RoomName, @event = "leave",
                    seq = _context.NextPresenceSequence(RoomName), player_id = player });
                return true;
            }
        }
        internal void QueueAdmissionJoin(Func<Task> send)
        {
            lock (_sync)
            {
                _queuedPresence++;
                _presenceTask = _presenceTask.ContinueWith(async _ =>
                {
                    try { await send().ConfigureAwait(false); }
                    finally { lock (_sync) _queuedPresence--; }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            }
        }

        internal string[] RosterSnapshot() { lock (_sync) return _roster.OrderBy(id => id, StringComparer.Ordinal).ToArray(); }
        internal static string ComputeRosterHash(IEnumerable<string> players)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", players.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)));
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        internal void RepairRoster()
        {
            lock (_sync)
            {
                if (_state == PlayServGameRoomState.Closed || _state == PlayServGameRoomState.Terminated) return;
                EnqueuePresence(new { type = "room_presence", room_name = RoomName, @event = "roster",
                    seq = _context.NextPresenceSequence(RoomName), players = RosterSnapshot().Select(id => new { player_id = id }).ToArray() });
            }
        }
        private void EnqueuePresence(object frame)
        {
            var uplink = PlayServGameServer.Uplink;
            if (uplink.State != PlayServUplinkState.Connected || _queuedPresence >= 256) return;
            _queuedPresence++;
            _presenceTask = _presenceTask.ContinueWith(async _ =>
            {
                try { await uplink.SendAsync(frame, _loopCancellation.Token).ConfigureAwait(false); }
                catch (PlayServGameServerException ex) { uplink.Report(ex.UnifiedError); }
                catch (OperationCanceledException) { }
                finally { lock (_sync) _queuedPresence--; }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
        internal Task DrainPresenceForTesting() { lock (_sync) return _presenceTask; }
        internal void ApplyConfiguration(PlayServRoomConfiguration configuration)
        {
            if (!_context.EnableUplink || configuration == null) return;
            lock (_sync)
            {
                if (_configuration != null && _configuration.Version >= configuration.Version) return;
                _configuration = configuration;
                _desiredSnapshot = _desiredSnapshot.WithCapacity(configuration.Capacity);
            }
        }
        internal void TerminateFromUplink(PlayServError error) => ApplyHeartbeatFailure(error);

        internal async Task EvaluateLifetimeAsync()
        {
            _connectionTracker?.Evaluate();
            string reason = null;
            lock (_sync)
            {
                if (_configuration == null || _state == PlayServGameRoomState.Closed ||
                    _state == PlayServGameRoomState.Terminated || _closeTask != null) return;
                var now = _context.MonotonicSeconds();
                if (_configuration.RoomLifetimeSeconds > 0 && now - _startedAt >= _configuration.RoomLifetimeSeconds)
                    reason = "room_lifetime_expired";
                else if (_configuration.RoomIdleTimeoutSeconds > 0 && _roster.Count == 0 &&
                    now - _emptySince >= _configuration.RoomIdleTimeoutSeconds.Value) reason = "room_idle_timeout";
            }
            if (reason == null) return;
            await CloseAsync().ConfigureAwait(false);
            _context.Post(() => LifetimeClosed?.Invoke(this, reason));
        }

        private async Task RunLifetimeAsync()
        {
            try
            {
                while (!_loopCancellation.IsCancellationRequested)
                {
                    await _context.Delay(TimeSpan.FromMilliseconds(250), _loopCancellation.Token).ConfigureAwait(false);
                    await EvaluateLifetimeAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch { PlayServGameServer.Uplink.Report(UplinkErrors.Exception("room_lifetime_close_failed").UnifiedError); }
        }

        public string FunctionSlug { get; }

        public string RoomName
        {
            get
            {
                lock (_sync)
                    return _desiredSnapshot.RoomName;
            }
        }

        public PlayServGameRoomSnapshot DesiredSnapshot
        {
            get
            {
                lock (_sync)
                    return _desiredSnapshot;
            }
        }

        public PlayServRoomPlacementAcknowledgment Placement
        {
            get
            {
                lock (_sync)
                    return _placement;
            }
        }

        public PlayServRoomPlacementAcknowledgment LastPlacementAcknowledgment => Placement;

        public PlayServGameRoomState State
        {
            get
            {
                lock (_sync)
                    return _state;
            }
        }

        public DateTimeOffset? LastHeartbeatAt
        {
            get
            {
                lock (_sync)
                    return _lastHeartbeatAt;
            }
        }

        public PlayServError LastError
        {
            get
            {
                lock (_sync)
                    return _lastError;
            }
        }

        /// <summary>Atomically replaces the desired room snapshot used by future heartbeats.</summary>
        public void Update(PlayServGameRoomSnapshot snapshot)
        {
            PlayServGameServer.ValidateSnapshot(snapshot, nameof(snapshot));
            lock (_sync)
            {
                ThrowIfClosedOrTerminated();
                if (!string.Equals(_desiredSnapshot.RoomName.Trim(), snapshot.RoomName.Trim(), StringComparison.Ordinal))
                    throw new ArgumentException("A room handle cannot change its room name.", nameof(snapshot));
                _desiredSnapshot = _configuration == null ? snapshot : snapshot.WithCapacity(_configuration.Capacity);
            }
        }

        /// <summary>Sends one immediate heartbeat, serialized with all other beats for this room.</summary>
        public async Task<PlayServRoomUpsertResult> HeartbeatAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
                ThrowIfClosedOrTerminated();

            await _heartbeatGate.WaitAsync(cancellationToken);
            try
            {
                PlayServGameRoomSnapshot snapshot;
                lock (_sync)
                {
                    ThrowIfClosedOrTerminated();
                    snapshot = _desiredSnapshot;
                }

                try
                {
                    var result = await PlayServGameServer.UpsertRoomCoreAsync(
                        _context,
                        FunctionSlug,
                        snapshot,
                        cancellationToken);
                    ApplyHeartbeatSuccess(result, _context.UtcNow());
                    return result;
                }
                catch (PlayServGameServerException ex)
                {
                    ApplyHeartbeatFailure(ex.UnifiedError);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    ApplyHeartbeatFailure(new PlayServError(
                        PlayServErrorCode.InvalidConfiguration,
                        "server_credential_resolution_failed",
                        "The PlayServ game server credential could not be resolved."));
                    throw;
                }
            }
            finally
            {
                _heartbeatGate.Release();
            }
        }

        /// <summary>Stops heartbeats and awaits the remote room close.</summary>
        public Task<PlayServGameRoomCloseResult> CloseAsync(
            CancellationToken cancellationToken = default)
        {
            var task = BeginClose(cancellationToken);
            _connectionTracker?.Dispose();
            return task;
        }

        private Task<PlayServGameRoomCloseResult> BeginClose(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_state == PlayServGameRoomState.Closed)
                {
                    return Task.FromResult(
                        new PlayServGameRoomCloseResult(true, true, PlayServError.None));
                }
                if (_state == PlayServGameRoomState.Terminated)
                {
                    return Task.FromResult(
                        new PlayServGameRoomCloseResult(false, true, _lastError));
                }
                if (_closeTask != null)
                    return _closeTask;

                _state = PlayServGameRoomState.Draining;
                _loopCancellation.Cancel();
                _remove(_registryKey, this);
                _closeTask = CloseCoreAsync(cancellationToken);
                return _closeTask;
            }
        }

        /// <summary>Starts a best-effort close. Prefer awaiting <see cref="CloseAsync"/>.</summary>
        public void Dispose()
        {
            Task<PlayServGameRoomCloseResult> close;
            try
            {
                close = CloseAsync();
            }
            catch
            {
                return;
            }

            ObserveBestEffort(close);
        }

        internal void ApplyInitialHeartbeat(PlayServRoomUpsertResult result, DateTimeOffset at)
        {
            ApplyHeartbeatSuccess(result, at);
        }

        internal void StartHeartbeatLoop()
        {
            lock (_sync)
            {
                if (_loopTask != null || _state == PlayServGameRoomState.Closed ||
                    _state == PlayServGameRoomState.Terminated)
                    return;
                _loopTask = RunHeartbeatLoopAsync(_loopCancellation.Token);
                if (_context.EnableUplink) _ = RunLifetimeAsync();
            }
        }

        internal void CancelHeartbeatLoop()
        {
            _loopCancellation.Cancel();
            _connectionTracker?.Dispose();
        }

        internal void CancelLocally()
        {
            _loopCancellation.Cancel();
            lock (_sync)
            {
                if (_state != PlayServGameRoomState.Terminated)
                    _state = PlayServGameRoomState.Closed;
            }
            _connectionTracker?.Dispose();
        }

        private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _context.Delay(_context.HeartbeatInterval, cancellationToken);
                    await HeartbeatAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (PlayServGameServerException)
                {
                    if (State == PlayServGameRoomState.Terminated)
                        return;
                }
                catch (Exception)
                {
                    ApplyHeartbeatFailure(new PlayServError(
                        PlayServErrorCode.InvalidConfiguration,
                        "server_credential_resolution_failed",
                        "The PlayServ game server credential could not be resolved."));
                    return;
                }
            }
        }

        private void ApplyHeartbeatSuccess(PlayServRoomUpsertResult result, DateTimeOffset at)
        {
            ApplyConfiguration(result.RoomConfiguration);
            if (_context.EnableUplink)
            {
                var repair = false;
                var diverged = false;
                lock (_sync)
                {
                    if (result.RosterCheck == "ok") { _mismatches = 0; _divergenceReported = false; }
                    else if (result.RosterCheck == "mismatch" && PlayServGameServer.Uplink.State == PlayServUplinkState.Connected)
                    {
                        repair = true;
                        if (++_mismatches >= 3 && !_divergenceReported) { _divergenceReported = true; diverged = true; }
                    }
                }
                if (repair) RepairRoster();
                if (diverged) _context.Post(() => PresenceDiverged?.Invoke(this));
            }
            Action<PlayServGameRoomHandle, PlayServRoomPlacementAcknowledgment> changed = null;
            lock (_sync)
            {
                if (_state == PlayServGameRoomState.Closed || _state == PlayServGameRoomState.Terminated)
                    return;
                if (_placement == null || !_placement.ContentEquals(result.Placement))
                    changed = PlacementChanged;
                _placement = result.Placement;
                _lastHeartbeatAt = at;
                _lastError = PlayServError.None;
                _state = result.Placement != null &&
                         (result.Placement.Draining || result.Placement.OpenRefused)
                    ? PlayServGameRoomState.Draining
                    : PlayServGameRoomState.Active;
            }

            if (changed != null)
            {
                try
                {
                    _context.Post(() => changed(this, result.Placement));
                }
                catch
                {
                }
            }
        }

        private void ApplyHeartbeatFailure(PlayServError error)
        {
            Action<PlayServGameRoomHandle, PlayServError> failed;
            Action<PlayServGameRoomHandle, PlayServError> terminated = null;
            var terminal = false;
            lock (_sync)
            {
                if (_state == PlayServGameRoomState.Closed || _state == PlayServGameRoomState.Terminated)
                    return;
                _lastError = error ?? PlayServError.None;
                failed = HeartbeatFailed;
                if (PlayServGameServer.IsTerminalHeartbeatError(error))
                {
                    _state = PlayServGameRoomState.Terminated;
                    terminal = true;
                    _loopCancellation.Cancel();
                    terminated = Terminated;
                }
                else
                {
                    _state = PlayServGameRoomState.Degraded;
                }
            }

            if (failed != null)
            {
                try
                {
                    _context.Post(() => failed(this, error));
                }
                catch
                {
                }
            }
            if (terminal)
            {
                _connectionTracker?.Dispose();
                _remove(_registryKey, this);
                try
                {
                    if (terminated != null) _context.Post(() => terminated(this, error));
                }
                catch
                {
                }
            }
        }

        private async Task<PlayServGameRoomCloseResult> CloseCoreAsync(CancellationToken cancellationToken)
        {
            PlayServError closeError = PlayServError.None;
            try
            {
                Task loop;
                lock (_sync)
                    loop = _loopTask;
                if (loop != null)
                {
                    try
                    {
                        await loop;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                await _heartbeatGate.WaitAsync(cancellationToken);
                _heartbeatGate.Release();
                await PlayServGameServer.CloseRoomCoreAsync(
                    _context,
                    FunctionSlug,
                    RoomName.Trim(),
                    cancellationToken);
            }
            catch (PlayServGameServerException ex)
            {
                closeError = ex.UnifiedError;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                closeError = new PlayServError(
                    PlayServErrorCode.InvalidConfiguration,
                    "server_credential_resolution_failed",
                    "The PlayServ game server credential could not be resolved.");
            }
            finally
            {
                lock (_sync)
                {
                    _state = PlayServGameRoomState.Closed;
                    if (closeError.IsError)
                        _lastError = closeError;
                }
            }

            return new PlayServGameRoomCloseResult(!closeError.IsError, false, closeError);
        }

        private void ThrowIfClosedOrTerminated()
        {
            if (_state == PlayServGameRoomState.Closed)
                throw new InvalidOperationException("The PlayServ room handle is closed.");
            if (_state == PlayServGameRoomState.Terminated)
                throw new InvalidOperationException("The PlayServ room handle was terminated by a non-retryable failure.");
        }

        private static async void ObserveBestEffort(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }
    }
}
