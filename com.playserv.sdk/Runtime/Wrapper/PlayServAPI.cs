using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;
using UnityEngine;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApi : IPlayServApi
    {
        private const string CONFIG_RESOURCE_NAME = "PlayServConfig";

        private PlayServImplementation? _instance;
        private PlayServConfig? _config;

        public string SdkVersion => SdkInfo.Version;

        public PlayServState State => _instance?.State ?? PlayServState.Offline;

        public event Action<TransportError>? OnTransportError;
        public event Action? OnKeepAlivePingSent;
        public event Action? OnKeepAlivePongReceived;

        public void Config(string gameAccessToken, string gameVersion, string? sdkVersion = null)
        {
            if (string.IsNullOrWhiteSpace(gameAccessToken))
                throw new ArgumentException("Game access token is required.", nameof(gameAccessToken));

            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("Game version is required.", nameof(gameVersion));

            _instance ??= new PlayServImplementation();
            _instance.SetConfig(gameAccessToken, gameVersion, sdkVersion);
            SubscribeToInstanceEvents();
        }

        public async Task<bool> Connect()
        {
            if (_instance == null)
            {
                _instance = new PlayServImplementation();
                SetConfigFromResources();
                SubscribeToInstanceEvents();
            }

            if (State is PlayServState.Online or PlayServState.Connecting or PlayServState.Handshaking)
                throw new InvalidOperationException("PlayServ is already connected or connecting.");

            return await _instance.Connect();
        }

        public IObservable<T> Subscribe<T>() =>
            Instance.Subscribe<T>();

        public IDisposable Subscribe<T>(Action<T> onNext) =>
            Instance.Subscribe(onNext);

        public void Send<T>(T command) =>
            Instance.Send(command);

        public Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Instance.GetTransportImplementation();

        public void Publish<T>(T @event) =>
            Instance.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) =>
            Instance.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) =>
            Instance.PublishForUser(userId, @event);

        public void Disconnect()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
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
            // Important: avoid double-subscribe if Config/Connect called multiple times
            // Simple approach: unsubscribe first (if instance supports it) or guard with a flag.
            // Here we guard by detaching and re-attaching if needed.

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

        private void SetConfigFromResources()
        {
            var config = GetOrLoadConfig();
            _instance ??= new PlayServImplementation();
            _instance.SetConfig(
                config.GameAccessToken,
                config.GameVersion,
                config.SdkVersion,
                config.AllowMultipleConnections,
                config.KeepAlivePingIntervalMs,
                config.KeepAlivePongTimeoutMs);
        }

        private PlayServConfig GetOrLoadConfig()
        {
            if (_config != null)
                return _config;

            _config = Resources.Load<PlayServConfig>(CONFIG_RESOURCE_NAME);
            if (_config == null)
            {
                throw new InvalidOperationException(
                    $"PlayServ config asset not found. Create a PlayServConfig asset in a Resources folder with name {CONFIG_RESOURCE_NAME}.");
            }

            return _config;
        }
    }
}