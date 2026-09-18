using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    /// <summary>One room visit. Call on Unity's context; create a new session after Closed/Failed.</summary>
    public sealed class PlayServRoomSession : IDisposable
    {
        private readonly Func<IPlayServRoomProtocol> _protocolFactory;
        private readonly Func<PlayServHostRoomRequest,CancellationToken,Task<PlayServMatchResult>> _host;
        private readonly Func<PlayServJoinRoomRequest,CancellationToken,Task<PlayServMatchResult>> _join;
        private readonly Func<PlayServGameConnectionOptions,PlayServGameConnection> _connect;
        private readonly Func<PlayServSettings> _settings;
        private readonly Func<TimeSpan,CancellationToken,Task> _delay;
        private readonly PlayServRoomSessionOptions _options;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
        private Attempt _active;
        private Task _leaveTask, _recoveryTask;
        private string _slug, _room, _player, _clientToken, _accessToken;
        private object _joinParams, _provider;
        private PlayServSettings _originalSettings;
        private bool _disposed;

        public PlayServRoomSession(Func<IPlayServRoomProtocol> protocolFactory, PlayServRoomSessionOptions options = null)
            : this(protocolFactory, options,
                (request, ct) => PlayServMatchmaking.HostRoomAsync(request, new PlayServRoomHostOptions { Timeout = TimeSpan.FromSeconds(60) }, ct),
                (request, ct) => PlayServMatchmaking.JoinRoomAsync(request, ct),
                config => PlayServMatchmaking.CreateGameConnection(config), () => PlayServ.Settings) { }

        internal PlayServRoomSession(Func<IPlayServRoomProtocol> protocol, PlayServRoomSessionOptions options,
            Func<PlayServHostRoomRequest,CancellationToken,Task<PlayServMatchResult>> host,
            Func<PlayServJoinRoomRequest,CancellationToken,Task<PlayServMatchResult>> join,
            Func<PlayServGameConnectionOptions,PlayServGameConnection> connect, Func<PlayServSettings> settings,
            Func<TimeSpan,CancellationToken,Task> delay = null)
        {
            _protocolFactory = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _host = host; _join = join; _connect = connect; _settings = settings; _delay = delay ?? AsyncOperationBudget.Delay;
            options = options ?? new PlayServRoomSessionOptions();
            foreach (var duration in new[] { options.EntryTimeout, options.RecoveryTimeout, options.ExitTimeout })
                if (duration <= TimeSpan.Zero || duration.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(options));
            if (options.MaxReconnectAttempts < 0 || options.MaxReconnectAttempts > 3) throw new ArgumentOutOfRangeException(nameof(options));
            var connection = options.Connection ?? new PlayServGameConnectionOptions();
            _options = new PlayServRoomSessionOptions
            {
                EntryTimeout = options.EntryTimeout, RecoveryTimeout = options.RecoveryTimeout,
                ExitTimeout = options.ExitTimeout, MaxReconnectAttempts = options.MaxReconnectAttempts,
                Connection = new PlayServGameConnectionOptions { Timeout = connection.Timeout, DisplayName = connection.DisplayName, AllowInsecureWebSocket = connection.AllowInsecureWebSocket }
            };
        }
        public PlayServRoomSessionState State { get; private set; }
        public PlayServMatchReservation Reservation { get; private set; }
        public PlayServError LastError { get; private set; }
        public event Action<PlayServRoomSessionState> StateChanged;
        public event Action<string> MessageReceived;

        public Task<PlayServMatchReservation> HostAsync(PlayServHostRoomRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var snapshot = new PlayServHostRoomRequest { FunctionSlug = request.FunctionSlug, Region = request.Region, Attributes = _json.Clone(request.Attributes) };
            return EnterAsync(snapshot.FunctionSlug, null, null, async token => RequireReservation(await _host(snapshot, token)), ct);
        }
        public Task<PlayServMatchReservation> JoinAsync(PlayServJoinRoomRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.RoomName)) throw new ArgumentException("Room name is required.", nameof(request));
            var snapshot = new PlayServJoinRoomRequest { FunctionSlug = request.FunctionSlug, RoomName = request.RoomName, Params = _json.Clone(request.Params) };
            return EnterAsync(snapshot.FunctionSlug, snapshot.RoomName, snapshot.Params, async token => RequireReservation(await _join(snapshot, token)), ct);
        }
        public Task<PlayServMatchReservation> ConnectAsync(string functionSlug, PlayServMatchReservation reservation, CancellationToken ct = default)
        {
            if (reservation == null) throw new ArgumentNullException(nameof(reservation));
            return EnterAsync(functionSlug, reservation.RoomName, null, token => Task.FromResult(reservation), ct);
        }
        private async Task<PlayServMatchReservation> EnterAsync(string slug, string room, object joinParams,
            Func<CancellationToken,Task<PlayServMatchReservation>> reserve, CancellationToken ct)
        {
            if (_disposed || State != PlayServRoomSessionState.Idle) throw new InvalidOperationException("This room session has already started.");
            if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Room function slug is required.", nameof(slug));
            ct.ThrowIfCancellationRequested();
            _slug = slug; _room = room; _joinParams = joinParams;
            _originalSettings = _settings(); _player = _originalSettings?.PlayerId; _clientToken = _originalSettings?.ClientToken;
            _provider = _originalSettings?.RuntimeTokenProvider; _accessToken = _originalSettings?.PlayerAccessToken;
            SetState(PlayServRoomSessionState.Entering);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            using var budget = new AsyncOperationBudget(_options.EntryTimeout, linked.Token);
            try
            {
                ValidateIdentity(); budget.Check();
                var reservation = await budget.RunAsync(reserve(budget.Token));
                await OpenAsync(reservation, budget);
                return Reservation;
            }
            catch (Exception error)
            {
                if (State != PlayServRoomSessionState.Closed) Fail(error);
                throw;
            }
        }
        private async Task OpenAsync(PlayServMatchReservation reservation, AsyncOperationBudget budget)
        {
            budget.Check(); ValidateIdentity();
            if (reservation == null || string.IsNullOrWhiteSpace(reservation.RoomName) ||
                (_room != null && reservation.RoomName != _room)) throw new PlayServRoomEntryRefusedException("room_mismatch");
            _room = reservation.RoomName;
            var protocol = _protocolFactory() ?? throw new InvalidOperationException("The protocol factory returned no adapter.");
            PlayServGameConnection connection;
            try { connection = _connect(_options.Connection); } catch { protocol.Dispose(); throw; }
            var attempt = new Attempt(connection, protocol);
            attempt.Context = new PlayServRoomProtocolContext(_room, _player,
                async (message, ct) =>
                {
                    using var sendBudget = new AsyncOperationBudget(_options.Connection.Timeout, ct);
                    await sendBudget.RunAsync(attempt.Opened.Task);
                    if (!ReferenceEquals(_active, attempt)) throw new ObjectDisposedException(nameof(PlayServRoomProtocolContext));
                    try { ValidateIdentity(); } catch (Exception error) { Fail(error); throw; }
                    await connection.SendTextAsync(message, ct);
                }, error => { if (ReferenceEquals(_active, attempt)) Fail(new PlayServGameConnectionException(error)); });
            _active = attempt;
            attempt.Message = text =>
            {
                if (!ReferenceEquals(_active, attempt) || State == PlayServRoomSessionState.Closed || State == PlayServRoomSessionState.Failed) return;
                try { ValidateIdentity(); attempt.Context.Receive(text); }
                catch (Exception error) { attempt.Lost.TrySetException(error); if (State == PlayServRoomSessionState.Ready) Fail(error); return; }
                if (State == PlayServRoomSessionState.Ready) Notify(MessageReceived, text);
            };
            attempt.Closed = error =>
            {
                if (!ReferenceEquals(_active, attempt)) return;
                var failure = new PlayServGameConnectionException(error ?? new PlayServError(PlayServErrorCode.Transport, "connection_closed", "The room connection closed."));
                attempt.Lost.TrySetException(failure);
                if (State == PlayServRoomSessionState.Ready)
                {
                    if (!CanRetry(failure)) { Fail(failure); return; }
                    SetState(PlayServRoomSessionState.Reconnecting);
                    _recoveryTask = RecoverAsync();
                }
            };
            connection.MessageReceived += attempt.Message; connection.Closed += attempt.Closed;
            try
            {
                // Subscribe the game protocol before opening: admission can arrive during the handshake send.
                var entry = protocol.EnterAsync(attempt.Context, budget.Token) ?? throw new InvalidOperationException("The protocol returned no entry task.");
                _ = Observe(entry); _ = Observe(attempt.Lost.Task);
                await budget.RunAsync(connection.ConnectAsync(reservation, budget.Token));
                attempt.Opened.TrySetResult(true);
                var completed = await budget.RunAsync(Task.WhenAny(entry, attempt.Lost.Task));
                await completed;
                budget.Check(); ValidateIdentity();
                if (!ReferenceEquals(_active, attempt) || attempt.Lost.Task.IsCompleted || connection.State == PlayServGameConnectionState.Closed)
                    throw new PlayServGameConnectionException(new PlayServError(PlayServErrorCode.Transport, "connection_closed", "The room connection closed during entry."));
                Reservation = reservation;
                SetState(PlayServRoomSessionState.Ready);
            }
            catch { if (ReferenceEquals(_active, attempt)) DropAttempt(); throw; }
        }
        private async Task RecoverAsync()
        {
            DropAttempt();
            using var budget = new AsyncOperationBudget(_options.RecoveryTimeout, _lifetime.Token);
            Exception last = new TimeoutException("Room recovery exhausted its attempts.");
            try
            {
                for (var count = 0; count < _options.MaxReconnectAttempts; count++)
                {
                    budget.Check(); ValidateIdentity();
                    try
                    {
                        var result = await budget.RunAsync(_join(new PlayServJoinRoomRequest { FunctionSlug = _slug, RoomName = _room, Params = _json.Clone(_joinParams) }, budget.Token));
                        if (!result.IsMatched)
                        {
                            if (result.Status != PlayServMatchStatus.Searching) throw new PlayServRoomEntryRefusedException("room_not_found");
                            if (count + 1 < _options.MaxReconnectAttempts)
                                await budget.RunAsync(_delay(TimeSpan.FromMilliseconds(Math.Max(1000, result.RetryAfterMs ?? 0)), budget.Token));
                            continue;
                        }
                        await OpenAsync(RequireReservation(result), budget);
                        return;
                    }
                    catch (Exception error) when (CanRetry(error))
                    {
                        last = error;
                        if (count + 1 < _options.MaxReconnectAttempts)
                            await budget.RunAsync(_delay((error as PlayServMatchmakingException)?.RetryAfter ?? TimeSpan.FromSeconds(1), budget.Token));
                    }
                }
                throw last;
            }
            catch (Exception error) { if (State != PlayServRoomSessionState.Closed) Fail(error); }
        }
        public Task SendTextAsync(string message, CancellationToken ct = default)
        {
            if (State != PlayServRoomSessionState.Ready || _active == null) throw new InvalidOperationException("The room is not ready.");
            return _active.Context.SendTextAsync(message, ct);
        }
        public Task LeaveAsync(CancellationToken ct = default) => _leaveTask ?? (_leaveTask = LeaveCoreAsync(ct));
        private async Task LeaveCoreAsync(CancellationToken ct)
        {
            SetState(PlayServRoomSessionState.Closed);
            CancelLifetime();
            var attempt = _active;
            try
            {
                if (attempt != null && attempt.Connection.State == PlayServGameConnectionState.HandshakeSent)
                {
                    using var budget = new AsyncOperationBudget(_options.ExitTimeout, ct);
                    await budget.RunAsync(attempt.Protocol.LeaveAsync(budget.Token));
                }
            }
            catch { /* Exit is best effort; local cleanup must always complete. */ }
            finally { DropAttempt(); }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; SetState(PlayServRoomSessionState.Closed); CancelLifetime(); DropAttempt();
        }
        private void CancelLifetime() { try { _lifetime.Cancel(); } catch (AggregateException) { } }
        private void DropAttempt()
        {
            var attempt = _active; _active = null;
            if (attempt == null) return;
            attempt.Connection.MessageReceived -= attempt.Message; attempt.Connection.Closed -= attempt.Closed;
            attempt.Context.Close(); attempt.Opened.TrySetCanceled(); attempt.Lost.TrySetCanceled();
            try { attempt.Protocol.Dispose(); } catch { }
            attempt.Connection.Dispose();
        }
        private void ValidateIdentity()
        {
            var current = _settings();
            if (current == null || !ReferenceEquals(current, _originalSettings) || current.PlayerId != _player ||
                string.IsNullOrWhiteSpace(_player) || current.ClientToken != _clientToken || !ReferenceEquals(current.RuntimeTokenProvider, _provider) ||
                (_provider == null && current.PlayerAccessToken != _accessToken) ||
                (_provider is PlayServPlayerSession managed && managed.PlayerId != _player))
                throw new PlayServRoomEntryRefusedException("player_context_changed");
        }
        private static PlayServMatchReservation RequireReservation(PlayServMatchResult result)
        {
            if (result?.IsMatched != true || result.Reservation == null) throw new PlayServRoomEntryRefusedException("room_not_ready");
            return result.Reservation;
        }
        private void Fail(Exception error)
        {
            if (State == PlayServRoomSessionState.Closed || State == PlayServRoomSessionState.Failed) return;
            LastError = (error as PlayServGameConnectionException)?.UnifiedError ?? (error as PlayServMatchmakingException)?.UnifiedError ??
                (error as PlayServRoomEntryRefusedException)?.UnifiedError ?? new PlayServError(error is TimeoutException ? PlayServErrorCode.Timeout : PlayServErrorCode.Transport,
                    "room_session_failed", "The room session could not complete.");
            SetState(PlayServRoomSessionState.Failed); CancelLifetime(); DropAttempt();
        }
        private static bool CanRetry(Exception error)
        {
            if (error is TimeoutException) return true;
            if (error is PlayServMatchmakingException matchmaking) return matchmaking.UnifiedError.Retryable;
            if (error is PlayServGameConnectionException connection)
                return (connection.UnifiedError.Code == PlayServErrorCode.Network || connection.UnifiedError.Code == PlayServErrorCode.Transport || connection.UnifiedError.Code == PlayServErrorCode.Timeout) &&
                    !(connection.UnifiedError.TransportCode >= 4000) && connection.UnifiedError.TransportCode != 1008;
            return false;
        }
        private void SetState(PlayServRoomSessionState state) { if (State != state) { State = state; Notify(StateChanged, state); } }
        private static void Notify<T>(Action<T> handlers, T value)
        { if (handlers != null) foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(value); } catch { } }
        private static async Task Observe(Task task) { try { await task; } catch { } }
        private sealed class Attempt
        {
            internal readonly PlayServGameConnection Connection; internal readonly IPlayServRoomProtocol Protocol;
            internal readonly TaskCompletionSource<bool> Opened = new TaskCompletionSource<bool>();
            internal readonly TaskCompletionSource<bool> Lost = new TaskCompletionSource<bool>();
            internal PlayServRoomProtocolContext Context; internal Action<string> Message; internal Action<PlayServError> Closed;
            internal Attempt(PlayServGameConnection connection, IPlayServRoomProtocol protocol) { Connection = connection; Protocol = protocol; }
        }
    }
}
