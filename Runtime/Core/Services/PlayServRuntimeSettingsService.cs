using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    internal sealed class PlayServRuntimeSettingsService
    {
        private readonly Func<PlayServSettings> _loadSettings;
        private readonly IPlayServRuntimeSessionFactory _sessionFactory;
        private readonly Func<PlayServSettings, PlayServTransportImplementationFactory> _createTransportImplementationFactory;
        private readonly Func<PlayServSettings, string> _buildTransportKey;
        private readonly Action<IPlayServRuntimeSession> _subscribeToInstanceEvents;
        private readonly Func<PlayServSettings, string, CancellationToken, Task<string>> _resolveLatestVersion;
        private readonly Action<string> _syncLoadedConfigGameVersion;
        private readonly Action<string> _logTrace;
        private readonly int _versionRefreshTimeoutSeconds;

        public PlayServRuntimeSettingsService(
            Func<PlayServSettings> loadSettings,
            IPlayServRuntimeSessionFactory sessionFactory,
            Func<PlayServSettings, PlayServTransportImplementationFactory> createTransportImplementationFactory,
            Func<PlayServSettings, string> buildTransportKey,
            Action<IPlayServRuntimeSession> subscribeToInstanceEvents,
            Func<PlayServSettings, string, CancellationToken, Task<string>> resolveLatestVersion,
            Action<string> syncLoadedConfigGameVersion,
            Action<string> logTrace,
            int versionRefreshTimeoutSeconds)
        {
            _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _createTransportImplementationFactory = createTransportImplementationFactory ?? throw new ArgumentNullException(nameof(createTransportImplementationFactory));
            _buildTransportKey = buildTransportKey ?? throw new ArgumentNullException(nameof(buildTransportKey));
            _subscribeToInstanceEvents = subscribeToInstanceEvents ?? throw new ArgumentNullException(nameof(subscribeToInstanceEvents));
            _resolveLatestVersion = resolveLatestVersion ?? throw new ArgumentNullException(nameof(resolveLatestVersion));
            _syncLoadedConfigGameVersion = syncLoadedConfigGameVersion ?? throw new ArgumentNullException(nameof(syncLoadedConfigGameVersion));
            _logTrace = logTrace ?? throw new ArgumentNullException(nameof(logTrace));
            _versionRefreshTimeoutSeconds = versionRefreshTimeoutSeconds;
        }

        public PlayServSettings GetOrCreateSettings(ref PlayServSettings currentSettings)
        {
            if (currentSettings != null)
                return currentSettings;

            currentSettings = _loadSettings() ?? new PlayServSettings();
            return currentSettings;
        }

        public void ApplySettings(
            PlayServSettings settings,
            ref PlayServSettings currentSettings,
            ref IPlayServRuntimeSession instance,
            ref string instanceEndpoint,
            ref string instanceTransportKey)
        {
            EnsureConfigured(settings);
            EnsureInstanceForSettings(settings, ref instance, ref instanceEndpoint, ref instanceTransportKey);

            instance.SetConfig(
                settings.ClientToken,
                settings.GameId,
                settings.UserId,
                settings.GameVersion,
                settings.SdkVersion,
                settings.AllowMultipleConnections,
                settings.KeepAlivePingIntervalMs,
                settings.KeepAlivePongTimeoutMs,
                settings.RuntimeTokenProvider,
                settings.PlayerAccessToken);

            _subscribeToInstanceEvents(instance);
            currentSettings = settings;
        }

        public async Task<PlayServSettings> RefreshConfiguredGameVersionAsync(
            PlayServSettings settings,
            CancellationToken ct = default)
        {
#if UNITY_5_3_OR_NEWER
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (string.IsNullOrWhiteSpace(settings.DeployApiServerAddress) ||
                string.IsNullOrWhiteSpace(settings.GameId))
            {
                return settings;
            }

            var timeoutSeconds = Math.Max(
                1,
                Math.Min(settings.TimeoutSeconds, _versionRefreshTimeoutSeconds));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            _logTrace(
                $"[PlayServ] Refreshing game version before connect. gameId={settings.GameId}, deployApi={settings.DeployApiServerAddress}, timeout={timeoutSeconds}s");

            var latestVersion = await _resolveLatestVersion(settings, settings.GameId, timeoutCts.Token);
            settings.GameVersion = latestVersion;
            _syncLoadedConfigGameVersion(latestVersion);
#endif
            return settings;
        }

        public static void EnsureConfigured(PlayServSettings settings)
        {
            if (!HasHandshakeCredential(settings))
            {
                throw new InvalidOperationException(
                    "A public ClientToken or IPlayServRuntimeTokenProvider is required for DataFlow/runtime-auth.");
            }

            PlayServCredentialPolicy.NormalizeClientToken(settings.ClientToken);

            if (string.IsNullOrWhiteSpace(settings.GameId))
                throw new InvalidOperationException("Game ID is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.UserId))
                throw new InvalidOperationException("User ID is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.GameVersion))
                throw new InvalidOperationException("Game version is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.Endpoint))
                throw new InvalidOperationException("Endpoint is required. Provide BackendServerAddress.");
        }

        private static bool HasHandshakeCredential(PlayServSettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings.ClientToken) ||
                   settings.RuntimeTokenProvider != null ||
                   !string.IsNullOrWhiteSpace(settings.PlayerAccessToken);
        }

        private void EnsureInstanceForSettings(
            PlayServSettings settings,
            ref IPlayServRuntimeSession instance,
            ref string instanceEndpoint,
            ref string instanceTransportKey)
        {
            var endpoint = settings.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(settings));

            var transportKey = _buildTransportKey(settings);
            if (instance == null)
            {
                var transportFactory = _createTransportImplementationFactory(settings);
                instance = _sessionFactory.Create(endpoint, transportFactory, PlayServModuleRegistry.RegisterDefaults);
                instanceEndpoint = endpoint;
                instanceTransportKey = transportKey;
                return;
            }

            if (string.Equals(instanceTransportKey, transportKey, StringComparison.Ordinal))
                return;

            instance.Dispose();
            var replacementTransportFactory = _createTransportImplementationFactory(settings);
            instance = _sessionFactory.Create(endpoint, replacementTransportFactory, PlayServModuleRegistry.RegisterDefaults);
            instanceEndpoint = endpoint;
            instanceTransportKey = transportKey;
        }
    }
}
