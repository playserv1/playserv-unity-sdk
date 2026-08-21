using System;
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
            _state = PlayServGameRoomState.Active;
        }

        /// <summary>Raised after a heartbeat failure has updated <see cref="State"/>.</summary>
        public event Action<PlayServGameRoomHandle, PlayServError> HeartbeatFailed;
        /// <summary>Raised when the backend placement acknowledgment changes.</summary>
        public event Action<PlayServGameRoomHandle, PlayServRoomPlacementAcknowledgment> PlacementChanged;
        /// <summary>Raised once after a non-retryable heartbeat failure terminates the handle.</summary>
        public event Action<PlayServGameRoomHandle, PlayServError> Terminated;

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
                _desiredSnapshot = snapshot;
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
            }
        }

        internal void CancelHeartbeatLoop()
        {
            _loopCancellation.Cancel();
        }

        internal void CancelLocally()
        {
            _loopCancellation.Cancel();
            lock (_sync)
            {
                if (_state != PlayServGameRoomState.Terminated)
                    _state = PlayServGameRoomState.Closed;
            }
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
                    changed(this, result.Placement);
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
            lock (_sync)
            {
                if (_state == PlayServGameRoomState.Closed || _state == PlayServGameRoomState.Terminated)
                    return;
                _lastError = error ?? PlayServError.None;
                failed = HeartbeatFailed;
                if (PlayServGameServer.IsTerminalHeartbeatError(error))
                {
                    _state = PlayServGameRoomState.Terminated;
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
                    failed(this, error);
                }
                catch
                {
                }
            }
            if (terminated != null)
            {
                _remove(_registryKey, this);
                try
                {
                    terminated(this, error);
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
