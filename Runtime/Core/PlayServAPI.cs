using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApi : IPlayServConnectionApi, IPlayServRuntimeAccess
    {
        private const int ConnectVersionRefreshTimeoutSeconds = 5;
        private int _shutdownIgnoreWarningLogged;

        private readonly IPlayServCommandDispatch _commandDispatch;
        private readonly PlayServApiConfigFacade _configFacade;
        private readonly PlayServApiConnectionOrchestrator _connectionOrchestrator;

        public string SdkVersion => SdkInfo.Version;

        public PlayServSettings Settings => _configFacade.Settings;

        public PlayServState State => _configFacade.State;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;

        public PlayServApi()
        {
            _commandDispatch = PlayServCommandDispatchFactory.Create();
            PlayServRuntimeHost.Configure(
                getRuntimeAccess: () => this,
                resolveJsonCodec: ResolveJsonCodec,
                getLocalExecution: () => _commandDispatch.LocalExecution,
                send: command => SendCommand(command),
                sendToModule: (command, moduleName) => SendCommand(command, moduleName));

            _configFacade = new PlayServApiConfigFacade(
                subscribeToInstanceEvents: SubscribeToInstanceEvents,
                logTrace: message => PlayServLog.Trace(PlayServLogCategory.General, message),
                versionRefreshTimeoutSeconds: ConnectVersionRefreshTimeoutSeconds);

            _connectionOrchestrator = new PlayServApiConnectionOrchestrator(
                getState: () => State,
                getOrCreateSettings: _configFacade.GetOrCreateSettings,
                refreshConfiguredGameVersionAsync: _configFacade.RefreshConfiguredGameVersionAsync,
                applySettings: _configFacade.ApplySettings,
                getCurrentSession: () => _configFacade.CurrentSession,
                disconnect: _configFacade.Disconnect,
                resetShutdownState: ResetShutdownState,
                logTrace: message => PlayServLog.Trace(PlayServLogCategory.General, message),
                shouldIgnoreMissingInstance: ShouldIgnoreMissingInstance,
                logShutdownIgnoreWarning: LogShutdownIgnoreWarning);
        }

        public void Config(PlayServSettings settings) => _configFacade.Config(settings);

        public void Config(
            string clientToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null) =>
            _configFacade.Config(clientToken, gameId, userId, gameVersion, sdkVersion);

        public void SetRuntimeTokenProvider(IPlayServRuntimeTokenProvider tokenProvider) =>
            _configFacade.SetRuntimeTokenProvider(tokenProvider);

        public Task<bool> Connect() => _connectionOrchestrator.ConnectAsync();

        public void Disconnect() => _configFacade.Disconnect();

        public void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory) =>
            _configFacade.SetWebRtcSignalingClientFactory(signalingClientFactory);

        public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            _configFacade.GetLatestVersionAsync(gameId, ct);

        public ITransportImplementation GetTransportImplementation() =>
            RequiredSession.GetTransportImplementation();

        public void SendCommand<T>(T command)
        {
            if (_commandDispatch.TryHandleCommand(command, moduleName: null, _configFacade.CurrentSession != null))
                return;

            if (!_connectionOrchestrator.TryGetSessionForFireAndForget("command send", out var session))
                return;

            session.Send(command);
        }

        public void SendCommand<T>(T command, string moduleName)
        {
            if (_commandDispatch.TryHandleCommand(command, moduleName, _configFacade.CurrentSession != null))
                return;

            if (!_connectionOrchestrator.TryGetSessionForFireAndForget("command send", out var session))
                return;

            session.Send(command, moduleName);
        }

        public bool HasCurrentInstance => _configFacade?.CurrentSession != null;

        public IPlayServCommandBus CurrentCommandBus => _configFacade?.CurrentSession;

        public IPlayServCommandBus RequiredCommandBus => RequiredSession;

        public IPlayServModuleServiceProvider CurrentModuleServices => _configFacade?.CurrentSession?.ModuleServices;

        public IPlayServModuleServiceProvider RequiredModuleServices => RequiredSession.ModuleServices;

        public IPlayServCommandBus GetCommandBusForFireAndForget(string operationName) =>
            GetSessionForFireAndForget(operationName);

        public IPlayServModuleServiceProvider GetModuleServicesForFireAndForget(string operationName)
        {
            var session = GetSessionForFireAndForget(operationName);
            return session?.ModuleServices;
        }

        public IJsonCodec ResolveJsonCodec()
        {
            var services = _configFacade.CurrentSession?.ModuleServices;
            if (services != null && services.TryGet<IJsonCodec>(out var jsonCodec))
                return jsonCodec;

            return null;
        }

        private IPlayServRuntimeSession RequiredSession =>
            _configFacade.CurrentSession ?? throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

        private IPlayServRuntimeSession GetSessionForFireAndForget(string operationName)
        {
            return _connectionOrchestrator.TryGetSessionForFireAndForget(operationName, out var session)
                ? session
                : null;
        }

        private void SubscribeToInstanceEvents(IPlayServRuntimeSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            session.OnTransportError -= HandleTransportError;
            session.OnKeepAlivePingSent -= HandleKeepAlivePingSent;
            session.OnKeepAlivePongReceived -= HandleKeepAlivePongReceived;
            session.OnModuleCommand -= HandleModuleCommand;

            session.OnTransportError += HandleTransportError;
            session.OnKeepAlivePingSent += HandleKeepAlivePingSent;
            session.OnKeepAlivePongReceived += HandleKeepAlivePongReceived;
            session.OnModuleCommand += HandleModuleCommand;
        }

        private void HandleTransportError(TransportError error) => OnTransportError?.Invoke(error);

        private void HandleKeepAlivePingSent() => OnKeepAlivePingSent?.Invoke();

        private void HandleKeepAlivePongReceived() => OnKeepAlivePongReceived?.Invoke();

        private void HandleModuleCommand(string commandName, object command)
        {
            PlayServRuntimeHost.NotifyModuleCommand(commandName, command);
        }

        private void LogShutdownIgnoreWarning(string operationName)
        {
            if (Interlocked.Exchange(ref _shutdownIgnoreWarningLogged, 1) != 0)
                return;

#if UNITY_5_3_OR_NEWER
            PlayServLog.Warning(
                PlayServLogCategory.General,
                $"Ignoring {operationName} because Unity is shutting down or exiting play mode.");
#endif
        }

#if UNITY_5_3_OR_NEWER
        private void ResetShutdownState()
        {
            PlayServRuntimeShutdownState.Reset();
            Interlocked.Exchange(ref _shutdownIgnoreWarningLogged, 0);
        }

        private bool ShouldIgnoreMissingInstance()
        {
            return !Application.isPlaying || PlayServRuntimeShutdownState.IsShuttingDown;
        }
#else
        private void ResetShutdownState()
        {
            Interlocked.Exchange(ref _shutdownIgnoreWarningLogged, 0);
        }

        private static bool ShouldIgnoreMissingInstance() => false;
#endif
    }
}
