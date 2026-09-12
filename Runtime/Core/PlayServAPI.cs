using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Identity;
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
    internal sealed class PlayServApi : IPlayServConnectionApi, IPlayServAuthApi, IPlayServRuntimeAccess
    {
        private const int ConnectVersionRefreshTimeoutSeconds = 5;
        private int _shutdownIgnoreWarningLogged;

        private readonly IPlayServCommandDispatch _commandDispatch;
        private readonly PlayServApiConfigFacade _configFacade;
        private readonly PlayServApiConnectionOrchestrator _connectionOrchestrator;
        private readonly PlayServPlayerAuthCoordinator _playerAuthCoordinator;
        private IPlayServTransportCloseInfoSource _transportCloseInfoSource;

        public string SdkVersion => SdkInfo.Version;

        public PlayServSettings Settings => _configFacade.Settings;

        public PlayServState State => _configFacade.State;

        public event Action<TransportError> OnTransportError;
        public event Action<PlayServError> OnError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;

        public bool IsLoggedIn => CurrentSession.IsLoggedIn;

        public string PlayerId => CurrentSession.PlayerId;

        public PlayServSessionKind SessionKind => CurrentSession.Kind;

        public PlayServSessionInfo CurrentSession => _playerAuthCoordinator.CurrentSession;

        public PlayServPlayerProfile CurrentPlayerProfile =>
            _playerAuthCoordinator.CurrentPlayerProfile;

        public event Action<PlayServSessionLostInfo> SessionLost
        {
            add => _playerAuthCoordinator.SessionLost += value;
            remove => _playerAuthCoordinator.SessionLost -= value;
        }

        public Task<PlayServAuthProvidersResult> GetProvidersAsync(
            CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.GetProvidersAsync(cancellationToken);

        public Task<PlayServPlayerProfileResult> GetCurrentPlayerProfileAsync(
            CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.GetCurrentPlayerProfileAsync(cancellationToken);

        public Task<PlayServPlayerProfileResult> RefreshCurrentPlayerProfileAsync(
            CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.RefreshCurrentPlayerProfileAsync(cancellationToken);

        public Task<PlayServAuthResult> LinkIdentityAsync(
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.LinkIdentityAsync(proof, cancellationToken);

        public Task<PlayServAuthResult> UnlinkIdentityAsync(
            string providerId,
            CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.UnlinkIdentityAsync(providerId, cancellationToken);

        public Task<PlayServAuthResult> MergeIdentityAsync(
            PlayServAuthConflict conflict,
            PlayServMergeChoice choice,
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.MergeIdentityAsync(conflict, choice, proof, cancellationToken);

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

            _playerAuthCoordinator = new PlayServPlayerAuthCoordinator(
                createHttpClient: settings => PlayServRuntimeHttpClientResolver.Create(
                    new PlayServHttpModuleContext(
                        settings.ToRuntimeSettings(),
                        PlayServJsonCompositionRoot.CreateDefaultJsonCodec())),
                defaultSessionStore: new PlayServPlayerPrefsSessionStore(),
                getSettings: _configFacade.GetOrCreateSettings,
                getState: () => State,
                refreshLiveAuthorization: (token, cancellationToken) =>
                    RequiredSession.RefreshPlayerAuthAsync(token, cancellationToken),
                disconnectTransport: DisconnectTransportForAuth,
                connectTransport: () => _connectionOrchestrator.ConnectAsync());

            _connectionOrchestrator = new PlayServApiConnectionOrchestrator(
                getState: () => State,
                getOrCreateSettings: _configFacade.GetOrCreateSettings,
                refreshConfiguredGameVersionAsync: _configFacade.RefreshConfiguredGameVersionAsync,
                preparePlayerAuthenticationAsync: _playerAuthCoordinator.PrepareSettingsForConnectAsync,
                applySettings: _configFacade.ApplySettings,
                handleConnected: _playerAuthCoordinator.HandleConnected,
                getCurrentSession: () => _configFacade.CurrentSession,
                disconnect: DisconnectInternal,
                resetShutdownState: ResetShutdownState,
                logTrace: message => PlayServLog.Trace(PlayServLogCategory.General, message),
                shouldIgnoreMissingInstance: ShouldIgnoreMissingInstance,
                logShutdownIgnoreWarning: LogShutdownIgnoreWarning);
        }

        public void Config(PlayServSettings settings)
        {
            _playerAuthCoordinator.StopRefreshLoop();
            _configFacade.Config(settings);
        }

        public void Config(
            string clientToken,
            string gameVersion,
            string sdkVersion = null)
        {
            _playerAuthCoordinator.StopRefreshLoop();
            _configFacade.Config(clientToken, gameVersion, sdkVersion);
        }

        public void SetRuntimeTokenProvider(IPlayServRuntimeTokenProvider tokenProvider)
        {
            _playerAuthCoordinator.StopRefreshLoop();
            _configFacade.SetRuntimeTokenProvider(tokenProvider);
        }

        public Task<bool> Connect() => _connectionOrchestrator.ConnectAsync();

        public Task<PlayServAuthResult> LoginExternalAsync(
            Playserv.Identity.PlayServExternalIdentityProof proof,
            PlayServExternalLoginMode mode,
            CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.LoginExternalAsync(proof, mode, cancellationToken);

        public Task<PlayServAuthResult> LogoutAsync(CancellationToken cancellationToken = default) =>
            _playerAuthCoordinator.LogoutAsync(cancellationToken);

        public Task<bool> RefreshPlayerAuthAsync(
            string newAccessToken,
            CancellationToken cancellationToken = default) =>
            RequiredSession.RefreshPlayerAuthAsync(newAccessToken, cancellationToken);

        public void Disconnect() => DisconnectInternal();

        public void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory) =>
            _configFacade.SetWebRtcSignalingClientFactory(signalingClientFactory);

        public Task<string> GetLatestVersionAsync(string deploymentId, CancellationToken ct = default) =>
            _configFacade.GetLatestVersionAsync(deploymentId, ct);

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

        private void DisconnectInternal()
        {
            _playerAuthCoordinator.StopRefreshLoop();
            DisconnectTransportForAuth();
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

            UnsubscribeFromTransportCloseInfo();
            _transportCloseInfoSource = session.GetTransportImplementation() as IPlayServTransportCloseInfoSource;
            if (_transportCloseInfoSource != null)
                _transportCloseInfoSource.Closed += HandleTransportClosed;
        }

        private void HandleTransportError(TransportError error)
        {
            OnTransportError?.Invoke(error);
            if (error?.UnifiedError != null)
                OnError?.Invoke(error.UnifiedError);
        }

        private void HandleKeepAlivePingSent() => OnKeepAlivePingSent?.Invoke();

        private void HandleKeepAlivePongReceived() => OnKeepAlivePongReceived?.Invoke();

        private void HandleModuleCommand(string commandName, object command)
        {
            PlayServRuntimeHost.NotifyModuleCommand(commandName, command);

            if (command is ParseErrorResponse parse)
            {
                OnError?.Invoke(new PlayServError(
                    PlayServErrorCode.Validation,
                    "parse_error",
                    parse.Error,
                    rawDetails: parse.ReceivedJson));
            }
            else if (command is ValidationErrorResponse validation)
            {
                OnError?.Invoke(new PlayServError(
                    PlayServErrorCode.Validation,
                    "validation_error",
                    validation.Error,
                    rawDetails: validation.ReceivedJson));
            }
            else if (command is CommandErrorResponse error &&
                     string.IsNullOrWhiteSpace(error.SourceCommand))
            {
                OnError?.Invoke(new PlayServError(
                    PlayServErrorCode.ServerError,
                    error.Error,
                    error.Message,
                    retryable: error.Retryable,
                    rawDetails: error.Details));
            }
        }

        private void HandleTransportClosed(PlayServTransportCloseInfo closeInfo) =>
            _playerAuthCoordinator.HandleTransportClosed(closeInfo);

        private void DisconnectTransportForAuth()
        {
            UnsubscribeFromTransportCloseInfo();
            _configFacade.Disconnect();
        }

        private void UnsubscribeFromTransportCloseInfo()
        {
            if (_transportCloseInfoSource == null)
                return;

            _transportCloseInfoSource.Closed -= HandleTransportClosed;
            _transportCloseInfoSource = null;
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
