using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiConfigFacade
    {
        private const string WebRtcScheme = "webrtc";
#if UNITY_5_3_OR_NEWER
        private const string ConfigResourceName = "PlayServConfig";
#endif

        private readonly PlayServRuntimeSettingsService _settingsService;
        private Func<PlayServRuntimeSettings, IWebRtcSignalingClient> _webRtcSignalingClientFactory;
        private PlayServImplementation _instance;
        private PlayServSettings _settings;
        private string _instanceEndpoint;
        private string _instanceTransportKey;

        public PlayServApiConfigFacade(
            Action<PlayServImplementation> subscribeToInstanceEvents,
            Action<string> logTrace,
            int versionRefreshTimeoutSeconds)
        {
            if (subscribeToInstanceEvents == null)
                throw new ArgumentNullException(nameof(subscribeToInstanceEvents));

            if (logTrace == null)
                throw new ArgumentNullException(nameof(logTrace));

            _settingsService = new PlayServRuntimeSettingsService(
                loadSettings: LoadSettingsFromUnityResources,
                createTransportImplementationFactory: CreateTransportImplementationFactory,
                buildTransportKey: BuildTransportKey,
                subscribeToInstanceEvents: subscribeToInstanceEvents,
                resolveLatestVersion: GetLatestVersionOrFallbackAsync,
                syncLoadedConfigGameVersion: SyncLoadedConfigGameVersion,
                logTrace: logTrace,
                versionRefreshTimeoutSeconds: versionRefreshTimeoutSeconds);
        }

        public PlayServSettings Settings => _settings;

        public PlayServImplementation CurrentInstance => _instance;

        public PlayServState State => _instance?.State ?? PlayServState.Offline;

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

        public void SetWebRtcSignalingClientFactory(Func<PlayServRuntimeSettings, IWebRtcSignalingClient> signalingClientFactory)
        {
            _webRtcSignalingClientFactory = signalingClientFactory;

            if (_instance != null && _settings != null && HasWebRtcScheme(_settings.Endpoint))
                Disconnect();
        }

        public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default)
        {
#if UNITY_5_3_OR_NEWER
            var settings = GetOrCreateSettings();
            return GetLatestVersionOrFallbackAsync(settings, gameId, ct);
#else
            return Task.FromException<string>(
                new PlatformNotSupportedException("Latest version lookup requires Unity runtime."));
#endif
        }

        public PlayServSettings GetOrCreateSettings()
        {
            return _settingsService.GetOrCreateSettings(ref _settings);
        }

        public void ApplySettings(PlayServSettings settings)
        {
            _settingsService.ApplySettings(
                settings,
                ref _settings,
                ref _instance,
                ref _instanceEndpoint,
                ref _instanceTransportKey);
        }

        public Task<PlayServSettings> RefreshConfiguredGameVersionAsync(PlayServSettings settings, CancellationToken ct = default)
        {
            return _settingsService.RefreshConfiguredGameVersionAsync(settings, ct);
        }

        public void Disconnect()
        {
            if (_instance == null)
                return;

            _instance.Dispose();
            _instance = null;
            _instanceEndpoint = null;
            _instanceTransportKey = null;
        }

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

            var transportSettings = settings.ToRuntimeSettings();
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
                var httpClient = PlayServRuntimeHttpClientResolver.Create(
                    new PlayServHttpModuleContext(settings.ToRuntimeSettings()));
                var latestVersion = await httpClient.GetLatestVersionAsync(gameId, ct);
                PlayServLog.Trace(PlayServLogCategory.Http, $"Latest game version resolved from deployment API: {latestVersion}");
                return latestVersion;
            }
            catch (Exception ex) when (!string.IsNullOrWhiteSpace(fallbackVersion))
            {
                PlayServLog.TraceWarning(
                    PlayServLogCategory.Http,
                    $"Failed to fetch latest game version for gameId={gameId}. " +
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
#else
        private static Task<string> GetLatestVersionOrFallbackAsync(
            PlayServSettings settings,
            string gameId,
            CancellationToken ct = default)
        {
            return Task.FromException<string>(
                new PlatformNotSupportedException("Latest version lookup requires Unity runtime."));
        }

        private static void SyncLoadedConfigGameVersion(string gameVersion)
        {
        }
#endif
    }
}
