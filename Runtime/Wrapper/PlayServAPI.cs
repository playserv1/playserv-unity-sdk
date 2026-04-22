using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Http.Common;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.WebRtc;
using Playserv.RPC;
using Playserv.Server;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApi : IPlayServApi
    {
        private const int ConnectVersionRefreshTimeoutSeconds = 5;
#if UNITY_5_3_OR_NEWER
        private const string ConfigResourceName = "PlayServConfig";
#endif
        private const string WebRtcScheme = "webrtc";
        private PlayServImplementation _instance;
        private PlayServSettings _settings;
        private string _instanceEndpoint;
        private string _instanceTransportKey;
        private Func<PlayServSettings, IWebRtcSignalingClient> _webRtcSignalingClientFactory;
        private int _shutdownIgnoreWarningLogged;
        private readonly PlayServApiLocalExecutionFacade _localExecution;
        private readonly PlayServApiRpcFacade _rpcFacade;
        private readonly PlayServRuntimeSettingsService _settingsService;
        private readonly PlayServApiConnectionOrchestrator _connectionOrchestrator;

        public string SdkVersion => SdkInfo.Version;
        public PlayServSettings Settings => _settings;

        public PlayServState State => _instance?.State ?? PlayServState.Offline;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;
        public event Action<InvokeRpcResponse> OnRpcInvokeResponse;

        public PlayServApi()
        {
            _localExecution = new PlayServApiLocalExecutionFacade();
            _settingsService = new PlayServRuntimeSettingsService(
                loadSettings: LoadSettingsFromUnityResources,
                createTransportImplementationFactory: CreateTransportImplementationFactory,
                buildTransportKey: BuildTransportKey,
                subscribeToInstanceEvents: SubscribeToInstanceEvents,
                resolveLatestVersion: GetLatestVersionOrFallbackAsync,
                syncLoadedConfigGameVersion: SyncLoadedConfigGameVersion,
                logTrace: ForwardLogTrace,
                versionRefreshTimeoutSeconds: ConnectVersionRefreshTimeoutSeconds);
            _connectionOrchestrator = new PlayServApiConnectionOrchestrator(
                getState: () => State,
                getOrCreateSettings: GetOrCreateSettings,
                refreshConfiguredGameVersionAsync: RefreshConfiguredGameVersionAsync,
                applySettings: ApplySettings,
                getCurrentInstance: () => _instance,
                disconnect: Disconnect,
                resetShutdownState: ResetShutdownState,
                logTrace: ForwardLogTrace,
                shouldIgnoreMissingInstance: ShouldIgnoreMissingInstance,
                logShutdownIgnoreWarning: LogShutdownIgnoreWarning);
            _rpcFacade = new PlayServApiRpcFacade(
                _localExecution,
                () => _instance,
                GetInstanceForFireAndForget);
        }

        public void Config(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _settings = settings.Clone();
            ApplySettings(_settings);
        }

        public void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string sdkVersion = null)
        {
            if (string.IsNullOrWhiteSpace(gameAccessToken))
                throw new ArgumentException("Game access token is required.", nameof(gameAccessToken));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID is required.", nameof(userId));

            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("Game version is required.", nameof(gameVersion));

            var settings = GetOrCreateSettings();
            settings.GameAccessToken = gameAccessToken;
            settings.GameId = gameId;
            settings.UserId = userId;
            settings.GameVersion = gameVersion;

            if (!string.IsNullOrWhiteSpace(sdkVersion))
                settings.SdkVersion = sdkVersion;

            ApplySettings(settings);
        }

        public Task<bool> Connect() => _connectionOrchestrator.ConnectAsync();

        public void SetWebRtcSignalingClientFactory(Func<PlayServSettings, IWebRtcSignalingClient> signalingClientFactory)
        {
            _webRtcSignalingClientFactory = signalingClientFactory;

            if (_instance != null && _settings != null && HasWebRtcScheme(_settings.Endpoint))
                Disconnect();
        }

        public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default)
        {
#if UNITY_5_3_OR_NEWER
            var settings = _settingsService.GetOrCreateSettings(ref _settings);
            return GetLatestVersionOrFallbackAsync(settings, gameId, ct);
#else
            return Task.FromException<string>(
                new PlatformNotSupportedException("Latest version lookup requires Unity runtime."));
#endif
        }

        public IDisposable Subscribe<T>(Action<T> onNext) =>
            _localExecution.TrySubscribe(onNext, _instance != null, out var subscription)
                ? subscription
                : Instance.Subscribe(onNext);

        public IObservable<T> Subscribe<T>() =>
            _localExecution.TrySubscribe<T>(_instance != null, out var observable)
                ? observable
                : Instance.Subscribe<T>();

        public void Send<T>(T command)
        {
            if (_localExecution.TryHandleCommand(command, moduleName: null, _instance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command);
        }

        public void SetCommandHandler(ICommandHandler commandHandler) =>
            _localExecution.SetCommandHandler(commandHandler);

        public void Send<T>(T command, string moduleName)
        {
            if (_localExecution.TryHandleCommand(command, moduleName, _instance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("command send", out var instance))
                return;

            instance.Send(command, moduleName);
        }

        public void SetEventHandler(IEventHandler eventHandler) =>
            _localExecution.SetEventHandler(eventHandler);

        public void Invoke(string serviceName, string methodName, object payload) =>
            _rpcFacade.Invoke(serviceName, methodName, payload);

        public void InvokeArgs(string serviceName, string methodName, params object[] args) =>
            _rpcFacade.InvokeArgs(serviceName, methodName, args);

        public void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>
            _rpcFacade.InvokeNamed(serviceName, methodName, payload);

        public void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            _localExecution.SetRpcInvoker(rpcInvoker);

        public void Invoke(string serviceName, string methodName, string payloadBase64) =>
            _rpcFacade.Invoke(serviceName, methodName, payloadBase64);

        public void Invoke<TService>(Expression<Action<TService>> method) =>
            _rpcFacade.Invoke(method);

        public void Invoke<TService>(Expression<Action<TService>> method, object payload) =>
            _rpcFacade.Invoke(method, payload);

        public void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>
            _rpcFacade.Invoke(method, payloadBase64);

        public Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Instance.GetTransportImplementation();

        public void Publish<T>(T @event)
        {
            if (_localExecution.TryPublish(@event, _instance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("event publish", out var instance))
                return;

            instance.Publish(@event);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            if (_localExecution.TryPublishForGroup(groupName, @event, _instance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("group event publish", out var instance))
                return;

            instance.PublishForGroup(groupName, @event);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            if (_localExecution.TryPublishForUser(userId, @event, _instance != null))
                return;

            if (!_connectionOrchestrator.TryGetInstanceForFireAndForget("user event publish", out var instance))
                return;

            instance.PublishForUser(userId, @event);
        }

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            Instance.SubscribeGroupAsync(groupName, ct);

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            Instance.UnsubscribeGroupAsync(groupName, ct);

#if UNITY_5_3_OR_NEWER
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Instance.Spawn(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Instance.Spawn(assetName, position);
#endif

        public void Disconnect()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
                _instanceEndpoint = null;
                _instanceTransportKey = null;
            }
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            Instance.SelectEntity(playerId, map, mode);

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default) =>
            Instance.GetDataByKeyAsync(key, query, variables, ct);

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null) =>
            Instance.StartDataByKeyPolling(key, query, variables, onData, onError);

        private PlayServImplementation Instance =>
            _instance ?? throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

        private void LogShutdownIgnoreWarning(string operationName)
        {
            if (Interlocked.Exchange(ref _shutdownIgnoreWarningLogged, 1) != 0)
                return;

#if UNITY_5_3_OR_NEWER
            Debug.LogWarning($"[PlayServ] Ignoring {operationName} because Unity is shutting down or exiting play mode.");
#endif
        }

        private void SubscribeToInstanceEvents(PlayServImplementation instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            instance.OnTransportError -= HandleTransportError;
            instance.OnKeepAlivePingSent -= HandleKeepAlivePingSent;
            instance.OnKeepAlivePongReceived -= HandleKeepAlivePongReceived;
            instance.OnRpcInvokeResponse -= HandleRpcInvokeResponse;

            instance.OnTransportError += HandleTransportError;
            instance.OnKeepAlivePingSent += HandleKeepAlivePingSent;
            instance.OnKeepAlivePongReceived += HandleKeepAlivePongReceived;
            instance.OnRpcInvokeResponse += HandleRpcInvokeResponse;
        }

        private void HandleTransportError(TransportError error) => OnTransportError?.Invoke(error);
        private void HandleKeepAlivePingSent() => OnKeepAlivePingSent?.Invoke();
        private void HandleKeepAlivePongReceived() => OnKeepAlivePongReceived?.Invoke();
        private void HandleRpcInvokeResponse(InvokeRpcResponse response) => OnRpcInvokeResponse?.Invoke(response);

        private void ApplySettings(PlayServSettings settings)
        {
            _settingsService.ApplySettings(
                settings,
                ref _settings,
                ref _instance,
                ref _instanceEndpoint,
                ref _instanceTransportKey);
        }

        private async Task<PlayServSettings> RefreshConfiguredGameVersionAsync(PlayServSettings settings, CancellationToken ct = default) =>
            await _settingsService.RefreshConfiguredGameVersionAsync(settings, ct);

        private PlayServSettings GetOrCreateSettings()
            => _settingsService.GetOrCreateSettings(ref _settings);

        private static bool TryLoadSettingsFromUnityResources(out PlayServSettings settings)
        {
            return PlayServSettingsResolver.TryLoadSettingsFromResourcesOrPackageDefaults(out settings);
        }

        private static PlayServSettings LoadSettingsFromUnityResources()
        {
            return TryLoadSettingsFromUnityResources(out var settings) ? settings : null;
        }

        private Func<string, ITransportImplementation> CreateTransportImplementationFactory(PlayServSettings settings)
        {
            if (!HasWebRtcScheme(settings?.Endpoint))
                return null;

            var transportSettings = settings.Clone();
            return endpoint => TransportImplementationResolver.Create(
                new TransportModuleContext(
                    endpoint,
                    logger: null,
                    settings: transportSettings,
                    webRtcSignalingClientFactory: _webRtcSignalingClientFactory));
        }

        private string BuildTransportKey(PlayServSettings settings)
        {
            var endpoint = settings?.Endpoint?.Trim() ?? string.Empty;
            if (!HasWebRtcScheme(endpoint))
                return endpoint;

            var signalingAddress = settings.WebRtcSignalingServerAddress?.Trim() ?? string.Empty;
            var channelLabel = settings.WebRtcDataChannelLabel?.Trim() ?? string.Empty;
            var iceServers = settings.WebRtcIceServers == null
                ? string.Empty
                : string.Join(";", settings.WebRtcIceServers);
            var hasFactory = _webRtcSignalingClientFactory != null ? "factory:on" : "factory:off";

            return $"{endpoint}|{signalingAddress}|{channelLabel}|{iceServers}|{hasFactory}";
        }

        private static bool HasWebRtcScheme(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return false;

            return string.Equals(uri.Scheme, WebRtcScheme, StringComparison.OrdinalIgnoreCase);
        }

#if UNITY_5_3_OR_NEWER
        private static async Task<string> GetLatestVersionOrFallbackAsync(
            PlayServSettings settings,
            string gameId,
            CancellationToken ct = default)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var fallbackVersion = settings.GameVersion?.Trim();

            try
            {
                var httpClient = PlayServRuntimeHttpClientResolver.Create(new PlayServHttpModuleContext(settings));
                var latestVersion = await httpClient.GetLatestVersionAsync(gameId, ct);
                LogTrace($"[PlayServ] Latest game version resolved from deployment API: {latestVersion}");
                return latestVersion;
            }
            catch (Exception ex) when (!string.IsNullOrWhiteSpace(fallbackVersion))
            {
                LogTraceWarning(
                    $"[PlayServ] Failed to fetch latest game version for gameId={gameId}. " +
                    $"Falling back to configured GameVersion={fallbackVersion}. Error: {ex.Message}");
                return fallbackVersion;
            }
        }

        private static void SyncLoadedConfigGameVersion(string gameVersion)
        {
            if (string.IsNullOrWhiteSpace(gameVersion))
                return;

            var config = Resources.Load<PlayServConfig>(ConfigResourceName);
            if (config == null)
                return;

            config.SetGameVersion(gameVersion);
        }

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

        private PlayServImplementation GetInstanceForFireAndForget(string operationName)
        {
            return _connectionOrchestrator.TryGetInstanceForFireAndForget(operationName, out var instance)
                ? instance
                : null;
        }

        private static void ForwardLogTrace(string message) => LogTrace(message);

        [System.Diagnostics.Conditional("PlayServ_Logs")]
        private static void LogTrace(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.Log(message);
#endif
        }

        [System.Diagnostics.Conditional("PlayServ_Logs")]
        private static void LogTraceWarning(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.LogWarning(message);
#endif
        }
    }
}
