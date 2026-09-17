using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>One process-owned dial-in session. Records realtime continues to use its independent /ws session.</summary>
    public sealed partial class PlayServGameServerUplink
    {
        private readonly object _sync = new object();
        private PlayServGameServerContext _context;
        private CancellationTokenSource _stop;
        private Task _run;
        private Task _disconnect;
        private TaskCompletionSource<bool> _ready;
        private IPlayServUplinkSocket _socket;
        private string _slug, _session;
        private double _sessionUntil, _lastPing;
        private PlayServUplinkState _state;
        private PlayServRoomConfiguration _configuration;
        private PlayServError _lastError = PlayServError.None;
        private bool _rpcWarned;
        internal Func<IPlayServUplinkSocket> SocketFactory = () => new PlayServUplinkSocket();
        internal Func<double> Seconds = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        internal Func<int, CancellationToken, Task> Delay = (ms, ct) => Task.Delay(ms, ct);

        public PlayServUplinkState State { get { lock (_sync) return _state; } }
        public PlayServError LastError { get { lock (_sync) return _lastError; } }
        public PlayServRoomConfiguration RoomConfiguration { get { lock (_sync) return _configuration; } }
        /// <summary>Safe normalized failure, delivered through the configuration's Unity synchronization context.</summary>
        public event Action<PlayServError> OnError;

        /// <summary>Connects without creating a room. Configuration must contain an executor slug.</summary>
        public Task ConnectAsync(CancellationToken ct = default) =>
            ConnectForRoomAsync(PlayServGameServer.GetContextForServices().ExecutorSlug, ct);

        internal Task ConnectForRoomAsync(string slug, CancellationToken ct)
        {
            PlayServGameServer.EnsureSupportedBuildForServices();
            ct.ThrowIfCancellationRequested();
            var context = PlayServGameServer.GetContextForServices();
            if (!context.EnableUplink) throw new InvalidOperationException("The server uplink is disabled.");
            if (string.IsNullOrWhiteSpace(slug) || !Regex.IsMatch(slug, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"))
                throw new ArgumentException("A valid executor slug is required.", nameof(slug));
            Task ready;
            lock (_sync)
            {
                if (_disconnect != null && !_disconnect.IsCompleted)
                    throw new InvalidOperationException("The uplink is disconnecting.");
                if (_slug != null && (!ReferenceEquals(_context, context) || _slug != slug))
                    throw new InvalidOperationException("One uplink can serve only one configured executor.");
                if (context.ExecutorSlug != null && context.ExecutorSlug != slug)
                    throw new InvalidOperationException("The room executor differs from the configured uplink executor.");
                if (_state == PlayServUplinkState.Terminated) throw new PlayServGameServerException(_lastError);
                if (_state == PlayServUplinkState.Connected && _session != null && Seconds() < _sessionUntil)
                    return Task.CompletedTask;
                if (_ready == null || _ready.Task.IsCompleted) _ready = NewReady();
                ready = _ready.Task;
                if (_run == null)
                {
                    _context = context; _slug = slug; _stop = new CancellationTokenSource();
                    context.UplinkStarted = true;
                    SetStateLocked(PlayServUplinkState.Connecting);
                    _run = Task.Run(() => RunAsync(_stop.Token));
                }
                else if (_state == PlayServUplinkState.Connected) _socket?.Abort();
            }
            // Cancellation belongs to the caller, not to other rooms sharing the process connection.
            return WaitAsync(ready, ct);
        }

        /// <summary>Stops reconnect and clears the session. It does not close rooms; prefer ShutdownAsync for that.</summary>
        public Task DisconnectAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CancelRoomCreation();
            lock (_sync)
            {
                if (_disconnect != null && !_disconnect.IsCompleted) return WaitAsync(_disconnect, ct);
                SetStateLocked(PlayServUplinkState.Disconnected); _session = null;
                EndDataConnection(UplinkErrors.Exception("uplink_disconnected", true));
                FailServiceQueries(UplinkErrors.Exception("uplink_disconnected", true));
                _stop?.Cancel(); _socket?.Abort(); _ready?.TrySetCanceled();
                _disconnect = DisconnectCoreAsync(_run);
                return WaitAsync(_disconnect, ct);
            }
        }
        private async Task DisconnectCoreAsync(Task run)
        {
            if (run != null) await run.ConfigureAwait(false);
            LoseAdmissionConnection(clear: true);
            await DrainRoomCreationAsync().ConfigureAwait(false);
            ResetRoomCreation();
            lock (_sync)
            {
                _run = null; _socket = null; _session = null; ClearConfigurationLocked();
                SetStateLocked(PlayServUplinkState.Disconnected);
                _slug = null; _context = null;
                _stop?.Dispose(); _stop = null; _lastError = PlayServError.None; _rpcWarned = false;
            }
        }

        internal async Task<string> GetRestCredentialAsync(PlayServGameServerContext context, CancellationToken ct)
        {
            string slug;
            lock (_sync)
            {
                if (_state == PlayServUplinkState.Terminated) throw new PlayServGameServerException(_lastError);
                if (ReferenceEquals(_context, context) && _session != null && Seconds() < _sessionUntil) return _session;
                if (_run == null || !ReferenceEquals(_context, context))
                    throw UplinkErrors.Exception("uplink_not_connected", true);
                slug = _slug;
            }
            await ConnectForRoomAsync(slug, ct).ConfigureAwait(false);
            lock (_sync)
            {
                if (_session == null || Seconds() >= _sessionUntil) throw UplinkErrors.Exception("uplink_session_expired", true);
                return _session;
            }
        }

        internal void ApplyConfiguration(RoomConfigWire wire)
        {
            if (wire == null) return;
            if (wire.capacity < 1 || wire.reservation_ttl_seconds < 1 || wire.room_lifetime_seconds < 0 ||
                wire.room_idle_timeout_seconds < 0 || wire.max_rooms < 1 || wire.version < 0)
                throw new PlayServUplinkFailure("uplink_invalid_room_config", true);
            PlayServRoomConfiguration next;
            lock (_sync)
            {
                if (_configuration != null && wire.version <= _configuration.Version) return;
                _configuration = next = new PlayServRoomConfiguration(wire);
            }
            PlayServGameServer.ApplyRoomConfiguration(next);
            QueueLifecycle(() => ConfigurationChanged?.Invoke(next));
        }

        internal Task SendAsync(object frame, CancellationToken ct = default) =>
            SendPreparedAsync(Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(frame)), ct);

        internal async Task SendPreparedAsync(byte[] bytes, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (bytes.Length > PlayServUplinkSocket.MaxFrameBytes) throw UplinkErrors.Exception("frame_too_large");
            IPlayServUplinkSocket socket;
            lock (_sync)
            {
                if (_state != PlayServUplinkState.Connected) throw UplinkErrors.Exception("uplink_not_connected", true);
                socket = _socket;
            }
            try { await socket.SendAsync(bytes, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { socket.Abort(); throw UplinkErrors.Exception("uplink_send_failed", true); }
        }

        internal void Report(PlayServError error)
        {
            PlayServGameServerContext context;
            lock (_sync) { _lastError = error; context = _context; }
            context?.Post(() => OnError?.Invoke(error));
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var failures = 0;
            var random = new Random();
            while (!ct.IsCancellationRequested)
            {
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                IPlayServUplinkSocket socket = null;
                Task monitor = null;
                try
                {
                    var resolution = ResolveCredentialAsync(_context, ct);
                    _ = resolution.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    await WaitAsync(resolution, ct).ConfigureAwait(false);
                    var credential = await resolution.ConfigureAwait(false);
                    socket = SocketFactory();
                    lock (_sync) _socket = socket;
                    var uri = new UriBuilder(_context.BackendServerAddress) { Path = "/uplink", Query = "", Fragment = "" };
                    uri.Scheme = uri.Scheme == "https" ? "wss" : "ws";
                    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        handshake.CancelAfter(TimeSpan.FromSeconds(10));
                        using var abort = handshake.Token.Register(socket.Abort);
                        await socket.ConnectAsync(uri.Uri, credential, handshake.Token).ConfigureAwait(false);
                        await socket.SendAsync(Encoding.UTF8.GetBytes(PlayServGameServerJson.Serialize(new
                        {
                            type = "uplink_hello", executor_slug = _slug, instance_id = _context.InstanceId,
                            protocol_version = 1, capabilities = Capabilities(), resume = true
                        })), handshake.Token).ConfigureAwait(false);
                        var ack = Parse(await socket.ReceiveAsync(handshake.Token).ConfigureAwait(false));
                        if (ack.type != "uplink_hello_ack")
                            throw new PlayServUplinkFailure("uplink_invalid_hello_ack", true);
                        AcceptSession(ack);
                        if (ack.room_config == null) { lock (_sync) ClearConfigurationLocked(); }
                        else ApplyConfiguration(ack.room_config);
                        SetAdmissionConnection(ack.admission, socket, cycle.Token);
                    }
                    lock (_sync)
                    {
                        BeginDataConnection(socket);
                        SetStateLocked(PlayServUplinkState.Connected); _lastPing = Seconds(); _lastError = PlayServError.None;
                        _ready.TrySetResult(true);
                    }
                    failures = 0;
                    monitor = MonitorAsync(socket, cycle.Token);
                    // Never wait for Unity callbacks in the receive loop: they can themselves await uplink I/O.
                    _context.Post(() => { if (ReferenceEquals(PlayServGameServer.Uplink, this)) PlayServGameServer.RepairUplinkRosters(); });
                    while (!ct.IsCancellationRequested)
                    {
                        var json = await socket.ReceiveAsync(cycle.Token).ConfigureAwait(false);
                        var frame = Parse(json);
                        switch (frame.type)
                        {
                            case "ping":
                                lock (_sync) _lastPing = Seconds();
                                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"pong\"}"), cycle.Token).ConfigureAwait(false);
                                break;
                            case "session_token": AcceptSession(frame); break;
                            case "ticket_offer": case "join_ack": HandleAdmissionFrame(json, frame.type, cycle.Token); break;
                            case "rpc_call": case "rpc_batch":
                                HandleRpcFrame(json, frame.type, socket, cycle.Token);
                                break;
                            case "leaderboard_result": HandleServiceReply(json, false); break;
                            case "data_result": HandleDataReply(json, socket, false); break;
                            case "frame_too_large":
                                HandleDataReply(json, socket, true);
                                HandleServiceReply(json, true);
                                Report(UplinkErrors.Exception("frame_too_large").UnifiedError); break;
                            default: HandleAdditionalFrame(json, frame.type, cycle.Token); break;
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var failure = ex as PlayServUplinkFailure;
                    var error = UplinkErrors.Exception(failure?.Code ?? "uplink_connection_lost", !(failure?.Terminal ?? false)).UnifiedError;
                    lock (_sync)
                    {
                        SetStateLocked(failure?.Terminal == true ? PlayServUplinkState.Terminated : PlayServUplinkState.Reconnecting);
                        _ready.TrySetException(new PlayServGameServerException(error));
                        if (failure?.Terminal == true) _session = null;
                    }
                    Report(error);
                    if (failure?.Terminal == true)
                    {
                        _context.Post(() => { if (ReferenceEquals(PlayServGameServer.Uplink, this)) PlayServGameServer.TerminateUplinkRooms(error); });
                        return;
                    }
                }
                catch (Exception) when (ct.IsCancellationRequested) { return; }
                finally
                {
                    LoseAdmissionConnection();
                    var queryError = LastError;
                    EndDataConnection(queryError.IsError ? new PlayServGameServerException(queryError) :
                        UplinkErrors.Exception("uplink_connection_lost", true));
                    FailServiceQueries(queryError.IsError ? new PlayServGameServerException(queryError) :
                        UplinkErrors.Exception("uplink_connection_lost", true));
                    cycle.Cancel(); socket?.Abort();
                    if (monitor != null) { try { await monitor.ConfigureAwait(false); } catch { } }
                    socket?.Dispose();
                }
                try
                {
                    var cap = Math.Min(10000, 250 * (1 << Math.Min(5, failures++)));
                    await Delay(random.Next(cap / 2, cap + 1), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task MonitorAsync(IPlayServUplinkSocket socket, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Delay(250, ct).ConfigureAwait(false);
                await SweepAdmissionsAsync().ConfigureAwait(false);
                lock (_sync)
                {
                    if (Seconds() - _lastPing >= 15 || Seconds() >= _sessionUntil) { socket.Abort(); return; }
                }
            }
        }

        private void AcceptSession(UplinkFrameWire frame)
        {
            if (string.IsNullOrWhiteSpace(frame.session_token) || frame.expires_in == null || frame.expires_in <= 0 ||
                frame.session_token.IndexOfAny(new[] { '\r', '\n', ' ' }) >= 0)
                throw new PlayServUplinkFailure("uplink_invalid_session", true);
            lock (_sync) { _session = frame.session_token; _sessionUntil = Seconds() + frame.expires_in.Value; }
        }
        private static UplinkFrameWire Parse(string json)
        {
            if (json == null || Encoding.UTF8.GetByteCount(json) > PlayServUplinkSocket.MaxFrameBytes)
                throw new PlayServUplinkFailure("frame_too_large", true);
            try
            {
                var type = PlayServGameServerJson.Deserialize<UplinkTypeWire>(json)?.type;
                // Ticket validation owns its strict JSON types; do not coerce expires_in through the session DTO.
                if (type == "ticket_offer" || type == "join_ack") return new UplinkFrameWire { type = type };
                var frame = PlayServGameServerJson.Deserialize<UplinkFrameWire>(json);
                if (string.IsNullOrWhiteSpace(frame?.type)) throw new Exception();
                return frame;
            }
            catch { throw new PlayServUplinkFailure("uplink_invalid_frame", true); }
        }

        private static async Task<string> ResolveCredentialAsync(PlayServGameServerContext context, CancellationToken ct)
        {
            try
            {
                var value = context.UplinkCredentialProvider != null
                    ? await context.UplinkCredentialProvider.GetCredentialAsync(ct).ConfigureAwait(false)
                    : Environment.GetEnvironmentVariable("PLAYSERV_DEPLOYMENT_TOKEN");
                if (context.UplinkCredentialProvider == null && string.IsNullOrEmpty(value))
                    value = await context.ServerKeyProvider.GetServerKeyAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(value) || value.Length > 16384 || Regex.IsMatch(value, @"\s|\p{Cc}"))
                    throw new Exception();
                if (value.StartsWith("sk_", StringComparison.Ordinal) && value.Length > 3) return value;
                var parts = value.Split('.');
                if (parts.Length != 3 || Array.Exists(parts, p => !Regex.IsMatch(p, "^[A-Za-z0-9_-]+$"))) throw new Exception();
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                var claims = PlayServGameServerJson.ParseOptionalObject(Encoding.UTF8.GetString(Convert.FromBase64String(payload))) as IDictionary<string, object>;
                if (claims == null || !claims.TryGetValue("sub", out var sub) || !(sub is string subject) ||
                    !subject.StartsWith("executor/", StringComparison.Ordinal) || !claims.ContainsKey("deployment_id")) throw new Exception();
                // Shape only, never a claim of signature validity. The platform authenticates the upgrade.
                return value;
            }
            catch (OperationCanceledException) { throw; }
            catch { throw new PlayServUplinkFailure("uplink_invalid_credential", true); }
        }

        private string[] Capabilities()
        {
            var capabilities = new List<string>();
            if (_context.EnablePushedAdmission) capabilities.Add("admission_push");
            if (_context.RoomFactory != null) capabilities.Add("room_create");
            if (_context.RpcMethods != null && _context.RpcMethods.Count > 0) capabilities.Add("rpc");
            return capabilities.ToArray();
        }
        partial void HandleAdditionalFrame(string json, string type, CancellationToken ct);
        internal static TaskCompletionSource<bool> NewReady() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal static async Task WaitAsync(Task task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled) { await task.ConfigureAwait(false); return; }
            var canceled = NewReady();
            using var registration = ct.Register(() => canceled.TrySetResult(true));
            if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task) ct.ThrowIfCancellationRequested();
            await task.ConfigureAwait(false);
        }
        internal void CancelForModuleShutdown()
        {
            CancelRoomCreation();
            lock (_sync)
            {
                SetStateLocked(PlayServUplinkState.Disconnected);
                EndDataConnection(UplinkErrors.Exception("uplink_disconnected", true));
                FailServiceQueries(UplinkErrors.Exception("uplink_disconnected", true));
                _stop?.Cancel(); _socket?.Abort();
            }
            // Reset the facade synchronously without blocking Unity's callback thread.
            PlayServGameServer.ReplaceUplinkForShutdown(this);
        }
    }
}
