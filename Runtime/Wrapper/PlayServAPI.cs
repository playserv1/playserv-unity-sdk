using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

#nullable enable

namespace Playserv.Wrapper
{
    internal sealed class PlayServApi : IPlayServApi
    {
#if UNITY_5_3_OR_NEWER
        private const string ConfigResourceName = "PlayServConfig";
#endif

        private PlayServImplementation? _instance;
        private PlayServSettings? _settings;
        private string? _instanceEndpoint;

        public string SdkVersion => SdkInfo.Version;

        public PlayServState State => _instance?.State ?? PlayServState.Offline;

        public event Action<TransportError>? OnTransportError;
        public event Action? OnKeepAlivePingSent;
        public event Action? OnKeepAlivePongReceived;

        public void Config(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _settings = settings.Clone();
            ApplySettings(_settings);
        }

        public void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string? sdkVersion = null)
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

        public async Task<bool> Connect()
        {
            if (State is PlayServState.Online or PlayServState.Connecting or PlayServState.Handshaking)
                throw new InvalidOperationException("PlayServ is already connected or connecting.");

            var settings = GetOrCreateSettings();
            ApplySettings(settings);
            return await Instance.Connect();
        }

        public IObservable<T> Subscribe<T>() =>
            Instance.Subscribe<T>();

        public IDisposable Subscribe<T>(Action<T> onNext) =>
            Instance.Subscribe(onNext);

        public void Send<T>(T command) => Instance.Send(command);

        public void Send<T>(T command, string moduleName) =>
            Instance.Send(command, moduleName);

        public Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Instance.GetTransportImplementation();

        public void Publish<T>(T @event) =>
            Instance.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) =>
            Instance.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) =>
            Instance.PublishForUser(userId, @event);

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
            }
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new() =>
            Instance.SelectEntity(playerId, map);

        private PlayServImplementation Instance =>
            _instance ?? throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

        private void SubscribeToInstanceEvents()
        {
            _instance!.OnTransportError -= HandleTransportError;
            _instance.OnKeepAlivePingSent -= HandleKeepAlivePingSent;
            _instance.OnKeepAlivePongReceived -= HandleKeepAlivePongReceived;

            _instance.OnTransportError += HandleTransportError;
            _instance.OnKeepAlivePingSent += HandleKeepAlivePingSent;
            _instance.OnKeepAlivePongReceived += HandleKeepAlivePongReceived;
        }

        private void HandleTransportError(TransportError error) => OnTransportError?.Invoke(error);
        private void HandleKeepAlivePingSent() => OnKeepAlivePingSent?.Invoke();
        private void HandleKeepAlivePongReceived() => OnKeepAlivePongReceived?.Invoke();

        private void ApplySettings(PlayServSettings settings)
        {
            EnsureConfigured(settings);
            EnsureInstanceForEndpoint(settings.Endpoint);

            Instance.SetConfig(
                settings.GameAccessToken,
                settings.GameId,
                settings.UserId,
                settings.GameVersion,
                settings.SdkVersion,
                settings.AllowMultipleConnections,
                settings.KeepAlivePingIntervalMs,
                settings.KeepAlivePongTimeoutMs);

            SubscribeToInstanceEvents();
        }

        private void EnsureInstanceForEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            if (_instance == null)
            {
                _instance = new PlayServImplementation(endpoint);
                _instanceEndpoint = endpoint;
                return;
            }

            if (string.Equals(_instanceEndpoint, endpoint, StringComparison.Ordinal))
                return;

            _instance.Dispose();
            _instance = new PlayServImplementation(endpoint);
            _instanceEndpoint = endpoint;
        }

        private PlayServSettings GetOrCreateSettings()
        {
            if (_settings != null)
                return _settings;

            if (TryLoadSettingsFromUnityResources(out var settings))
            {
                _settings = settings;
                return _settings;
            }

            _settings = new PlayServSettings();
            return _settings;
        }

        private static void EnsureConfigured(PlayServSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.GameAccessToken))
                throw new InvalidOperationException("Game access token is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.GameId))
                throw new InvalidOperationException("Game ID is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.UserId))
                throw new InvalidOperationException("User ID is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.GameVersion))
                throw new InvalidOperationException("Game version is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.Endpoint))
                throw new InvalidOperationException("Endpoint is required. Provide LocalEndpoint or RemoteEndpoint.");
        }

        private static bool TryLoadSettingsFromUnityResources(out PlayServSettings settings)
        {
#if UNITY_5_3_OR_NEWER
            var config = Resources.Load<PlayServConfig>(ConfigResourceName);
            if (config != null)
            {
                settings = config.ToSettings();
                return true;
            }
#endif

            settings = null!;
            return false;
        }
    }
}

#nullable restore
