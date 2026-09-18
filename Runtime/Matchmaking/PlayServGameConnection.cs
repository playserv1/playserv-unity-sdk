using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    /// <summary>
    /// One opt-in direct WebSocket to a C# game server, independent of the platform /ws.
    /// Connect success means only open plus handshake sent. The game owns its admission messages.
    /// Create and call from the Unity context. A new attempt requires a new instance and a usable reservation.
    /// </summary>
    public sealed class PlayServGameConnection : IDisposable
    {
        private const int MaxTextBytes = 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        private readonly TimeSpan _timeout;
        private readonly string _displayName;
        private readonly bool _allowInsecure;
        private readonly Func<PlayServSettings> _settings;
        private readonly Func<TransportModuleContext, ITransportImplementation> _createTransport;
        private readonly IJsonCodec _json;
        private readonly SynchronizationContext _context;
        private PlayServGameConnectionState _state;
        private ITransportImplementation _transport;
        private IDisposable _subscription;
        private IPlayServTransportCloseInfoSource _closeSource;

        internal PlayServGameConnection(PlayServGameConnectionOptions options, Func<PlayServSettings> settings,
            Func<TransportModuleContext, ITransportImplementation> createTransport, IJsonCodec json, SynchronizationContext context)
        {
            options = options ?? new PlayServGameConnectionOptions();
            if (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > int.MaxValue)
                throw Error(PlayServErrorCode.Validation, "invalid_timeout", "Connection timeout must be positive and bounded.");
            _timeout = options.Timeout;
            _displayName = options.DisplayName;
            _allowInsecure = options.AllowInsecureWebSocket;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _createTransport = createTransport ?? throw new ArgumentNullException(nameof(createTransport));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _context = context ?? new SynchronizationContext();
        }

        /// <summary>Plain game text, including early messages. It is never interpreted as admission confirmation.</summary>
        public event Action<string> MessageReceived;
        /// <summary>One close notification on the captured context. Null means explicit local Disconnect/Dispose.</summary>
        public event Action<PlayServError> Closed;
        public PlayServGameConnectionState State { get { lock (_gate) return _state; } }

        /// <summary>
        /// Sends the existing C# server handshake once, using the current player identity. No anonymous fallback,
        /// new reservation, polling or retry. Cancellation after send cannot roll back remote admission.
        /// A null remaining lifetime stays unknown; only a known expired ticket is rejected locally.
        /// </summary>
        public async Task ConnectAsync(PlayServMatchReservation reservation, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_state != PlayServGameConnectionState.Created)
                    throw Error(PlayServErrorCode.Conflict, "connection_already_used", "Use a new game connection for each attempt.");
                _state = PlayServGameConnectionState.Connecting;
            }

            using var budget = new OperationBudget(_timeout, ct, _closed.Token);
            try
            {
                ValidateReservation(reservation);
                var endpoint = ResolveEndpoint(reservation.Connect);
                var settings = _settings();
                var identity = new Identity(settings);
                identity.Validate(_settings());
                string bearer;
                try
                {
                    bearer = identity.Provider is PlayServPlayerSession managed
                        ? await AwaitBounded(managed.GetExistingPlayerTokenAsync(budget.Token), budget.Token)
                        : identity.Provider != null
                            ? await AwaitBounded(identity.Provider.GetTokenAsync(budget.Token), budget.Token)
                            : identity.Token;
                }
                catch (OperationCanceledException) { throw; }
                catch { throw Error(PlayServErrorCode.Unauthorized, "player_session_unavailable", "The existing player session could not be refreshed."); }
                budget.Token.ThrowIfCancellationRequested();
                identity.Validate(_settings());
                if (string.IsNullOrWhiteSpace(bearer) || bearer.StartsWith("pk_", StringComparison.Ordinal) || bearer.StartsWith("sk_", StringComparison.Ordinal))
                    throw Error(PlayServErrorCode.Unauthorized, "player_session_unavailable", "An existing player bearer is required.");
                var payload = new Dictionary<string, object> { ["playerId"] = identity.PlayerId };
                if (_displayName != null) payload["displayName"] = _displayName;
                payload["token"] = bearer;
                payload["reservationToken"] = reservation.ReservationToken;
                byte[] handshake;
                try { handshake = Utf8.GetBytes(_json.Serialize(payload)); }
                catch { throw Error(PlayServErrorCode.Serialization, "handshake_serialization_failed", "The game handshake could not be serialized."); }
                if (handshake.Length > 4096)
                    throw Error(PlayServErrorCode.Validation, "handshake_too_large", "The game handshake exceeds 4096 UTF-8 bytes.");

                budget.Token.ThrowIfCancellationRequested();
                ValidateReservation(reservation);
                var transport = _createTransport(new TransportModuleContext(endpoint, SilentLogger.Instance,
                    jsonCodec: _json));
                bool alreadyClosed;
                lock (_gate)
                {
                    alreadyClosed = _state == PlayServGameConnectionState.Closed;
                    if (!alreadyClosed) _transport = transport;
                }
                if (alreadyClosed)
                {
                    transport.Dispose();
                    throw Error(PlayServErrorCode.Transport, "connection_closed", "The game connection is closed.");
                }
                Attach(transport);
                budget.Token.ThrowIfCancellationRequested();
                if (!await AwaitBounded(transport.Connect(), budget.Token))
                    throw Error(PlayServErrorCode.Transport, "connection_failed", "The game WebSocket could not be opened.");
                budget.Token.ThrowIfCancellationRequested();
                identity.Validate(_settings());
                ValidateReservation(reservation);
                await SendCore(handshake, budget.Token, true, () => CancellationError(ct, budget.TimedOut).UnifiedError);
                budget.Token.ThrowIfCancellationRequested();
                identity.Validate(_settings());
                lock (_gate)
                {
                    if (_state == PlayServGameConnectionState.Closed)
                        throw Error(PlayServErrorCode.Transport, "connection_closed", "The game connection closed during its handshake.");
                    _state = PlayServGameConnectionState.HandshakeSent;
                }
            }
            catch (OperationCanceledException)
            {
                var failure = CancellationError(ct, budget.TimedOut);
                Close(failure.UnifiedError);
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                throw failure;
            }
            catch (PlayServGameConnectionException exception) { Close(exception.UnifiedError); throw; }
            catch
            {
                var failure = Error(PlayServErrorCode.Transport, "connection_failed", "The game connection failed.");
                Close(failure.UnifiedError);
                throw failure;
            }
        }

        /// <summary>
        /// Sends at most 1 MiB of UTF-8 text, serialized with other sends. Completion is transport send only.
        /// An ambiguous send is never repeated; timeout/cancellation after send starts closes this connection.
        /// </summary>
        public async Task SendTextAsync(string message, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (message == null) throw Error(PlayServErrorCode.Validation, "invalid_message", "A text message is required.");
            byte[] bytes;
            try
            {
                if (Utf8.GetByteCount(message) > MaxTextBytes)
                    throw Error(PlayServErrorCode.Validation, "message_too_large", "The text message exceeds 1 MiB.");
                bytes = Utf8.GetBytes(message);
            }
            catch (PlayServGameConnectionException) { throw; }
            catch { throw Error(PlayServErrorCode.Validation, "invalid_message", "The message must be valid UTF-8 text."); }
            using var budget = new OperationBudget(_timeout, ct, _closed.Token);
            try { await SendCore(bytes, budget.Token, false, () => CancellationError(ct, budget.TimedOut).UnifiedError); }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                throw CancellationError(ct, budget.TimedOut);
            }
        }

        private async Task SendCore(byte[] bytes, CancellationToken ct, bool handshake, Func<PlayServError> canceledError)
        {
            await _sendGate.WaitAsync(ct);
            bool started = false;
            try
            {
                ITransportImplementation transport;
                lock (_gate)
                {
                    var required = handshake ? PlayServGameConnectionState.Connecting : PlayServGameConnectionState.HandshakeSent;
                    if (_state != required || _transport == null)
                        throw Error(PlayServErrorCode.Transport, "connection_not_open", "The game connection is not ready to send.");
                    transport = _transport;
                }
                ct.ThrowIfCancellationRequested();
                started = true;
                await AwaitBounded(transport.Send(bytes), ct);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                if (started) Close(canceledError());
                throw;
            }
            catch (PlayServGameConnectionException) { throw; }
            catch
            {
                var failure = Error(PlayServErrorCode.Transport, "send_failed", "The game send failed; its outcome may be unknown.");
                if (started) Close(failure.UnifiedError);
                throw failure;
            }
            finally { _sendGate.Release(); }
        }

        /// <summary>Stops only this direct socket. Does not sign out, close platform /ws or return a reservation.</summary>
        public void Disconnect() => Close(null);
        public void Dispose() => Disconnect();

        private void Attach(ITransportImplementation transport)
        {
            var source = transport as IPlayServTransportCloseInfoSource;
            if (source != null) source.Closed += TransportClosed;
            IDisposable subscription;
            try { subscription = transport.OnReceive().Subscribe(new Receiver(this)); }
            catch
            {
                if (source != null) source.Closed -= TransportClosed;
                throw;
            }
            bool closed;
            lock (_gate)
            {
                closed = _state == PlayServGameConnectionState.Closed;
                if (!closed) { _closeSource = source; _subscription = subscription; }
            }
            if (closed)
            {
                if (source != null) source.Closed -= TransportClosed;
                subscription.Dispose();
            }
        }

        private void TransportClosed(PlayServTransportCloseInfo info) => Close(new PlayServError(
            PlayServErrorCode.Transport, "remote_closed", "The game server closed the connection.", transportCode: info?.StatusCode));

        private void Receive(byte[] bytes)
        {
            if (bytes == null || bytes.Length > MaxTextBytes)
            {
                Close(new PlayServError(PlayServErrorCode.Transport, "message_too_large", "The game message exceeds 1 MiB."));
                return;
            }
            string text;
            try { text = Utf8.GetString(bytes); }
            catch { Close(new PlayServError(PlayServErrorCode.InvalidResponse, "invalid_message", "The game message is not UTF-8 text.")); return; }
            Dispatch(() =>
            {
                if (State == PlayServGameConnectionState.Closed) return;
                Invoke(MessageReceived, text);
            });
        }

        private void Close(PlayServError error)
        {
            ITransportImplementation transport;
            IDisposable subscription;
            IPlayServTransportCloseInfoSource source;
            lock (_gate)
            {
                if (_state == PlayServGameConnectionState.Closed) return;
                _state = PlayServGameConnectionState.Closed;
                transport = _transport; _transport = null;
                subscription = _subscription; _subscription = null;
                source = _closeSource; _closeSource = null;
            }
            SafeCancel(_closed);
            if (source != null) source.Closed -= TransportClosed;
            try { subscription?.Dispose(); } catch { }
            try { transport?.Dispose(); } catch { }
            Dispatch(() => Invoke(Closed, error));
        }

        private void Dispatch(Action action) => _context.Post(_ => action(), null);
        private static void Invoke<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); } catch { /* Game-owned callbacks cannot break connection cleanup. */ }
        }

        private PlayServGameConnectionException CancellationError(CancellationToken caller, bool timedOut)
        {
            if (caller.IsCancellationRequested)
                return Error(PlayServErrorCode.Canceled, "connection_canceled", "The game connection was canceled.");
            if (!timedOut && _closed.IsCancellationRequested)
                return Error(PlayServErrorCode.Transport, "connection_closed", "The game connection is closed.");
            return Error(PlayServErrorCode.Timeout, "connection_timeout", "The game connection operation exceeded its budget.");
        }

        private static void ValidateReservation(PlayServMatchReservation reservation)
        {
            if (reservation == null || string.IsNullOrWhiteSpace(reservation.ReservationToken) || string.IsNullOrWhiteSpace(reservation.RoomName))
                throw Error(PlayServErrorCode.Validation, "invalid_reservation", "A room reservation is required.");
            if (reservation.RemainingLifetime.HasValue && reservation.RemainingLifetime.Value <= TimeSpan.Zero)
                throw Error(PlayServErrorCode.Validation, "reservation_expired", "The reservation has expired; request a fresh ticket explicitly.");
        }

        private string ResolveEndpoint(PlayServRoomConnect connect)
        {
            if (connect == null) throw Error(PlayServErrorCode.Validation, "connect_unavailable", "The reservation has no game endpoint.");
            var scheme = connect.Transport?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(scheme) && scheme != "ws" && scheme != "wss")
                throw Error(PlayServErrorCode.Validation, "unsupported_transport", "The direct connector requires ws or wss.");
            Uri uri;
            if (!string.IsNullOrEmpty(connect.ConnectString))
            {
                if (!Uri.TryCreate(connect.ConnectString, UriKind.Absolute, out uri))
                    throw Error(PlayServErrorCode.Validation, "invalid_endpoint", "The game endpoint is invalid.");
            }
            else
            {
                if (string.IsNullOrEmpty(scheme) || string.IsNullOrWhiteSpace(connect.Host) || connect.Port < 1 || connect.Port > 65535)
                    throw Error(PlayServErrorCode.Validation, "invalid_endpoint", "An explicit WebSocket host and port are required.");
                try { uri = new UriBuilder(scheme, connect.Host, connect.Port, "/").Uri; }
                catch { throw Error(PlayServErrorCode.Validation, "invalid_endpoint", "The game endpoint is invalid."); }
            }
            if ((uri.Scheme != "ws" && uri.Scheme != "wss") || string.IsNullOrEmpty(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                (!string.IsNullOrEmpty(scheme) && scheme != uri.Scheme) ||
                (!string.IsNullOrEmpty(connect.Host) && !string.Equals(connect.Host.Trim('[', ']'), uri.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)) ||
                (connect.Port != 0 && connect.Port != uri.Port))
                throw Error(PlayServErrorCode.Validation, "invalid_endpoint", "Game endpoint metadata is invalid or conflicting; URL credentials, queries and fragments are unsupported.");
            if (uri.Scheme == "ws" && !_allowInsecure)
                throw Error(PlayServErrorCode.Validation, "insecure_endpoint", "Plaintext WebSocket requires explicit development opt-in.");
            return uri.AbsoluteUri;
        }

        private static async Task AwaitBounded(Task task, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) ObserveFault(task);
            ct.ThrowIfCancellationRequested();
#if UNITY_WEBGL && !UNITY_EDITOR
            await AwaitWithoutWorkerThreads(task, ct);
#else
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task)
                {
                    ObserveFault(task);
                    ct.ThrowIfCancellationRequested();
                }
                await task;
                ct.ThrowIfCancellationRequested();
            }
#endif
        }

        internal static async Task AwaitWithoutWorkerThreads(Task task, CancellationToken ct)
        {
            while (!task.IsCompleted)
            {
                if (ct.IsCancellationRequested)
                {
                    ObserveFault(task);
                    ct.ThrowIfCancellationRequested();
                }
                await Task.Yield();
            }
            if (ct.IsCancellationRequested) ObserveFault(task);
            ct.ThrowIfCancellationRequested();
            await task;
        }
        private static async Task<T> AwaitBounded<T>(Task<T> task, CancellationToken ct)
        { await AwaitBounded((Task)task, ct); return await task; }

        private static void ObserveFault(Task task) => _ = task.ContinueWith(t => { var ignored = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        private static void SafeCancel(CancellationTokenSource source)
        {
            try { source.Cancel(); }
            catch (AggregateException) { /* A caller-owned provider callback must not interrupt cleanup. */ }
            catch (ObjectDisposedException) { }
        }

        private sealed class OperationBudget : IDisposable
        {
            private readonly CancellationTokenSource _watchStop = new CancellationTokenSource();
            private readonly CancellationTokenSource _source = new CancellationTokenSource();
            private readonly TaskCompletionSource<bool> _finished = new TaskCompletionSource<bool>();
            private readonly CancellationTokenRegistration _caller, _closed;
            private int _timedOut, _disposed;
            internal OperationBudget(TimeSpan timeout, CancellationToken caller, CancellationToken closed)
            {
                _caller = caller.Register(() => SafeCancel(_source));
                _closed = closed.Register(() => SafeCancel(_source));
                _ = WatchAsync((int)Math.Ceiling(timeout.TotalMilliseconds));
            }
            internal CancellationToken Token => _source.Token;
            internal bool TimedOut => Volatile.Read(ref _timedOut) != 0;
            private async Task WatchAsync(int timeoutMs)
            {
                try
                {
                    if (!await AsyncTimeoutHelper.WaitForCompletionOrTimeoutAsync(_finished.Task, timeoutMs, _watchStop.Token) &&
                        Volatile.Read(ref _disposed) == 0)
                    {
                        Interlocked.Exchange(ref _timedOut, 1);
                        SafeCancel(_source);
                    }
                }
                catch (OperationCanceledException) { }
            }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _finished.TrySetResult(true);
                SafeCancel(_watchStop);
                _caller.Dispose(); _closed.Dispose();
                _watchStop.Dispose(); _source.Dispose();
            }
        }

        private static PlayServGameConnectionException Error(PlayServErrorCode code, string source, string message) =>
            new PlayServGameConnectionException(new PlayServError(code, source, message));

        private sealed class Identity
        {
            private readonly PlayServSettings _settings;
            private readonly string _clientToken;
            internal readonly string PlayerId, Token;
            internal readonly IPlayServRuntimeTokenProvider Provider;
            internal Identity(PlayServSettings settings)
            {
                _settings = settings;
                _clientToken = settings?.ClientToken;
                PlayerId = settings?.PlayerId;
                Provider = settings?.RuntimeTokenProvider;
                Token = settings?.PlayerAccessToken;
                if (string.IsNullOrWhiteSpace(PlayerId))
                    throw Error(PlayServErrorCode.Unauthorized, "player_session_unavailable", "Sign in before connecting to a game server.");
            }
            internal void Validate(PlayServSettings current)
            {
                if (!ReferenceEquals(current, _settings) || current.PlayerId != PlayerId || current.ClientToken != _clientToken ||
                    !ReferenceEquals(current.RuntimeTokenProvider, Provider) ||
                    (Provider == null && current.PlayerAccessToken != Token) ||
                    (Provider is PlayServPlayerSession managed && managed.PlayerId != PlayerId))
                    throw Error(PlayServErrorCode.Unauthorized, "player_session_changed", "The player session changed; request a fresh reservation explicitly.");
            }
        }
        private sealed class Receiver : IObserver<byte[]>
        {
            private readonly PlayServGameConnection _owner;
            internal Receiver(PlayServGameConnection owner) => _owner = owner;
            public void OnNext(byte[] value) => _owner.Receive(value);
            public void OnCompleted() => _owner.Close(new PlayServError(PlayServErrorCode.Transport, "remote_closed", "The game connection ended."));
            public void OnError(Exception error) => _owner.Close(new PlayServError(PlayServErrorCode.Transport, "receive_failed", "The game connection could not receive a message."));
        }
        private sealed class SilentLogger : ILogger
        {
            internal static readonly SilentLogger Instance = new SilentLogger();
            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
    }
}
