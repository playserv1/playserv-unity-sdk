using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Game-owned listener. Start completes when ready; Stop must be idempotent and clean partial startup.</summary>
    public interface IPlayServHostedRoomTransport
    {
        Task StartAsync(PlayServHostedLaunch launch, CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    public sealed class PlayServHostedRoomOptions
    {
        public string Transport { get; set; } = "udp";
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public PlayServGameServerOptions ServerOptions { get; set; } = new PlayServGameServerOptions();

        internal PlayServGameServerOptions Configure(PlayServHostedLaunch launch)
        {
            ValidateTimeout(StartupTimeout, nameof(StartupTimeout));
            ValidateTimeout(CleanupTimeout, nameof(CleanupTimeout));
            var server = (ServerOptions ?? throw new ArgumentNullException(nameof(ServerOptions))).Copy();
            if (!server.EnableUplink || server.RoomFactory != null || server.UplinkCredentialProvider != null)
                throw new ArgumentException("Hosted startup requires its deployment credential, an enabled uplink and no multi-room factory.");
            if ((server.BackendServerAddress != null && server.BackendServerAddress != launch.ApiUrl) ||
                (server.ExecutorSlug != null && server.ExecutorSlug != launch.ExecutorSlug) ||
                (server.InstanceId != null && server.InstanceId != launch.DeploymentId))
                throw new ArgumentException("Server options conflict with the hosted launch identity.");
            server.BackendServerAddress = launch.ApiUrl;
            server.ExecutorSlug = launch.ExecutorSlug;
            server.InstanceId = launch.DeploymentId;
            server.UplinkCredentialProvider = launch.Credential;
            return server;
        }
        private static void ValidateTimeout(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(name, "A positive, bounded timeout is required.");
        }
    }

    /// <summary>Owns one launched room and its listener. Await StopAsync before the process exits.</summary>
    public sealed class PlayServHostedRoom
    {
        private readonly object _sync = new object();
        private readonly IPlayServHostedRoomTransport _transport;
        private readonly TimeSpan _startupTimeout, _cleanupTimeout;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private Task _listenerStart;
        private bool _listenerInvoked, _unknownRegistration, _registrationInFlight;
        private Task<PlayServGameServerShutdownResult> _stopping;
        internal PlayServGameServerContext Context;
        internal PlayServGameServerUplink Uplink;

        internal PlayServHostedRoom(IPlayServHostedRoomTransport transport, PlayServHostedRoomOptions options, PlayServHostedLaunch launch)
        { _transport = transport; _startupTimeout = options.StartupTimeout; _cleanupTimeout = options.CleanupTimeout; Launch = launch; }
        public PlayServHostedLaunch Launch { get; }
        public PlayServGameRoomHandle Room { get; private set; }

        internal async Task<PlayServHostedRoom> StartAsync(CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            deadline.CancelAfter(_startupTimeout);
            var ct = deadline.Token;
            var registering = false;
            try
            {
                await Uplink.ConnectAsync(ct);
                if (Uplink.RoomConfiguration == null) throw UplinkErrors.Exception("room_configuration_missing");
                _listenerStart = Context.InvokeAsync(async () =>
                {
                    ct.ThrowIfCancellationRequested();
                    _listenerInvoked = true;
                    try { await _transport.StartAsync(Launch, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch { throw HostedError("hosted_listener_start_failed"); }
                    return true;
                });
                Observe(_listenerStart);
                await PlayServGameServerUplink.WaitAsync(_listenerStart, ct);
                ct.ThrowIfCancellationRequested();
                registering = true;
                _registrationInFlight = true;
                var registration = PlayServGameServer.StartRoomCoreAsync(new PlayServStartRoomRequest(Launch.ExecutorSlug,
                    new PlayServGameRoomSnapshot(Launch.RoomName, 0, Uplink.RoomConfiguration.Capacity,
                        attributes: Launch.Attributes, connect: Launch.Connect)), ct, true);
                Observe(registration);
                await PlayServGameServerUplink.WaitAsync(registration, ct);
                Room = await registration;
                _registrationInFlight = false;
                ct.ThrowIfCancellationRequested();
                if (Room.State != PlayServGameRoomState.Active) throw HostedError("hosted_room_not_active");
                return this;
            }
            catch (Exception error)
            {
                var serverError = (error as PlayServGameServerException)?.UnifiedError;
                var definiteRefusal = serverError?.HttpStatus >= 400 && serverError.HttpStatus < 500 &&
                    serverError.HttpStatus != 408;
                _unknownRegistration = registering && Room == null && !definiteRefusal;
                _registrationInFlight = false;
                await StopWithResultAsync();
                if (_unknownRegistration) throw HostedError("room_registration_outcome_unknown");
                if (error is OperationCanceledException) throw;
                if (error is PlayServGameServerException) throw;
                throw HostedError("hosted_start_failed");
            }
        }

        public async Task StopAsync()
        {
            var result = await StopWithResultAsync();
            if (!result.IsSuccess) throw HostedError("hosted_cleanup_failed");
        }

        internal Task<PlayServGameServerShutdownResult> StopWithResultAsync()
        {
            TaskCompletionSource<PlayServGameServerShutdownResult> completion;
            lock (_sync)
            {
                if (_stopping != null) return _stopping;
                completion = new TaskCompletionSource<PlayServGameServerShutdownResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopping = completion.Task;
                _unknownRegistration |= _registrationInFlight && Room == null;
            }
            _stop.Cancel();
            Observe(StopCoreAsync(completion));
            return completion.Task;
        }

        private async Task StopCoreAsync(TaskCompletionSource<PlayServGameServerShutdownResult> completion)
        {
            var failed = false;
            PlayServGameServerShutdownResult result = null;
            using var cleanup = new CancellationTokenSource(_cleanupTimeout);
            try
            {
                if (_listenerStart != null)
                {
                    try { await PlayServGameServerUplink.WaitAsync(_listenerStart, cleanup.Token); }
                    catch { }
                }
                // A timed-out Start may still bind later. Keep the launch reserved until its final cleanup.
                var pendingStart = _listenerStart != null && !_listenerStart.IsCompleted;
                if (pendingStart) Observe(CleanupLateListenerAsync(_listenerStart));
                if (_listenerInvoked)
                {
                    using var listenerCleanup = new CancellationTokenSource(_cleanupTimeout);
                    try { await StopListenerAsync(listenerCleanup.Token); }
                    catch { failed = true; }
                }
                using var serverCleanup = new CancellationTokenSource(_cleanupTimeout);
                if (PlayServGameServer.OwnsHostedRoom(this))
                {
                    try
                    {
                        var shutdown = PlayServGameServer.ShutdownCoreAsync(serverCleanup.Token);
                        Observe(shutdown);
                        await PlayServGameServerUplink.WaitAsync(shutdown, serverCleanup.Token);
                        result = await shutdown;
                    }
                    catch { failed = true; }
                }
                failed |= result != null && !result.IsSuccess;
                PlayServGameServer.FinishHostedCleanup(this, _unknownRegistration || pendingStart || failed);
            }
            catch { failed = true; PlayServGameServer.FinishHostedCleanup(this, true); }
            finally
            {
                completion.TrySetResult(failed ? new PlayServGameServerShutdownResult(
                    Array.Empty<PlayServGameRoomCloseResult>(), HostedError("hosted_cleanup_failed").UnifiedError) :
                    result ?? new PlayServGameServerShutdownResult(Array.Empty<PlayServGameRoomCloseResult>(), PlayServError.None));
            }
        }

        private async Task StopListenerAsync(CancellationToken ct)
        {
            var stopping = Context.InvokeAsync(async () => { await _transport.StopAsync(ct); return true; });
            Observe(stopping);
            await PlayServGameServerUplink.WaitAsync(stopping, ct);
        }

        private async Task CleanupLateListenerAsync(Task start)
        {
            try { await start; } catch { }
            // Let the first cleanup settle before the second Stop, so neither can outlive a new launch.
            Task<PlayServGameServerShutdownResult> initial;
            lock (_sync) initial = _stopping;
            var initialSucceeded = initial != null && (await initial).IsSuccess;
            using var timeout = new CancellationTokenSource(_cleanupTimeout);
            try
            {
                await StopListenerAsync(timeout.Token);
                if (initialSucceeded && !_unknownRegistration) PlayServGameServer.ReleaseHostedRoom(this);
            }
            catch { }
        }

        internal void RoomRemoved(PlayServGameRoomHandle room)
        {
            if (ReferenceEquals(Room, room)) Context.Post(() => Observe(StopWithResultAsync()));
        }
        internal void CancelForModuleShutdown() => Observe(StopWithResultAsync());
        internal static PlayServGameServerException HostedError(string code) =>
            new PlayServGameServerException(new PlayServError(PlayServErrorCode.InvalidConfiguration, code,
                "Hosted room operation failed: " + code + "."));
        private static void Observe(Task task) =>
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

}
