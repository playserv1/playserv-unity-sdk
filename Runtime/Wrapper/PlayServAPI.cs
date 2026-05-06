using System;
using System.Threading;
using System.Threading.Tasks;
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
    internal sealed class PlayServApi : IPlayServConnectionApi
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

        public PlayServImplementation CurrentInstance => _configFacade.CurrentInstance;

        public PlayServImplementation RequiredInstance => Instance;

        public PlayServApi()
        {
            _commandDispatch = PlayServCommandDispatchFactory.Create();
            PlayServRuntimeHost.Configure(
                getCurrentInstance: () => _configFacade?.CurrentInstance,
                getRequiredInstance: () => Instance,
                getInstanceForFireAndForget: GetInstanceForFireAndForget,
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
                getCurrentInstance: () => _configFacade.CurrentInstance,
                disconnect: _configFacade.Disconnect,
                resetShutdownState: ResetShutdownState,
                logTrace: message => PlayServLog.Trace(PlayServLogCategory.General, message),
                shouldIgnoreMissingInstance: ShouldIgnoreMissingInstance,
                logShutdownIgnoreWarning: LogShutdownIgnoreWarning);
        }

        public void Config(PlayServSettings settings) => _configFacade.Config(settings);

        public void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string sdkVersion = null) =>
            _configFacade.Config(gameAccessToken, gameId, userId, gameVersion, sdkVersion);

        public Task<bool> Connect() => _connectionOrchestrator.ConnectAsync();

        public void Disconnect() => _configFacade.Disconnect();

        public void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory) =>
            _configFacade.SetWebRtcSignalingClientFactory(signalingClientFactory);

        public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
            _configFacade.GetLatestVersionAsync(gameId, ct);

        public ITransportImplementation GetTransportImplementation() =>
            Instance.GetTransportImplementation();

        public void SendCommand<T>(T command)
        {
            if (_commandDispatch.TryHandleCommand(command, moduleName: null, _configFacade.CurrentInstance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command);
        }

        public void SendCommand<T>(T command, string moduleName)
        {
            if (_commandDispatch.TryHandleCommand(command, moduleName, _configFacade.CurrentInstance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command, moduleName);
        }

        public bool TryGetInstanceForFireAndForget(string operationName, out PlayServImplementation instance) =>
            _connectionOrchestrator.TryGetInstanceForFireAndForget(operationName, out instance);

        public IJsonCodec ResolveJsonCodec()
        {
            var instance = _configFacade.CurrentInstance;
            if (instance != null && instance.ModuleServices.TryGet<IJsonCodec>(out var jsonCodec))
                return jsonCodec;

            return null;
        }

        private PlayServImplementation Instance =>
            _configFacade.CurrentInstance ?? throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

        private PlayServImplementation GetInstanceForFireAndForget(string operationName)
        {
            return _connectionOrchestrator.TryGetInstanceForFireAndForget(operationName, out var instance)
                ? instance
                : null;
        }

        private void SubscribeToInstanceEvents(PlayServImplementation instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            instance.OnTransportError -= HandleTransportError;
            instance.OnKeepAlivePingSent -= HandleKeepAlivePingSent;
            instance.OnKeepAlivePongReceived -= HandleKeepAlivePongReceived;
            instance.OnModuleCommand -= HandleModuleCommand;

            instance.OnTransportError += HandleTransportError;
            instance.OnKeepAlivePingSent += HandleKeepAlivePingSent;
            instance.OnKeepAlivePongReceived += HandleKeepAlivePongReceived;
            instance.OnModuleCommand += HandleModuleCommand;
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
