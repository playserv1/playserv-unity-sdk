using System;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// Persistent bootstrap component for configuring and connecting PlayServ in samples.
    /// </summary>
    public sealed class PlayServBootstrapSample : MonoBehaviour
    {
        private static PlayServBootstrapSample _instance;
        private static bool _applicationIsQuitting;

        [Header("Credentials")]
        [SerializeField] private string gameAccessToken = "your-token";
        [SerializeField] private string gameId = "game-001";
        [SerializeField] private string userId = "player-001";
        [SerializeField] private string gameVersion = "1.0.0";

        [Header("Endpoint")]
        [SerializeField] private string remoteEndpoint = PlayServSettings.DefaultRemoteEndpoint;

        [Header("Behavior")]
        [SerializeField] private bool autoConnect;
        [SerializeField] private bool disconnectOnDestroy = true;

        [Header("KeepAlive")]
        [SerializeField] private int keepAlivePingIntervalMs = 5000;
        [SerializeField] private int keepAlivePongTimeoutMs = 5000;

        private bool _isOwner;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _applicationIsQuitting = false;
            _instance = this;
            _isOwner = true;
            DontDestroyOnLoad(gameObject);
        }

        public void ConnectToCurrentServer(string accessToken, string gameId, string userId)
        {
            gameAccessToken = accessToken;
            this.gameId = gameId;
            this.userId = userId;
            
            if (!_isOwner)
                return;

            Configure();

            if (autoConnect &&
                PlayServ.State != PlayServState.Online &&
                PlayServ.State != PlayServState.Connecting &&
                PlayServ.State != PlayServState.Handshaking)
            {
                _ = ConnectAsync();
            }
        }

        private void OnEnable()
        {
            if (!_isOwner)
                return;

            PlayServ.OnTransportError += OnTransportError;
        }

        private void OnDisable()
        {
            if (!_isOwner)
                return;

            PlayServ.OnTransportError -= OnTransportError;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            if (_isOwner && disconnectOnDestroy && _applicationIsQuitting)
                PlayServ.Disconnect();
        }

        private void OnApplicationQuit()
        {
            _applicationIsQuitting = true;
        }

        [ContextMenu("Configure SDK")]
        public void Configure()
        {
            var settings = new PlayServSettings
            {
                GameAccessToken = gameAccessToken,
                GameId = gameId,
                UserId = userId,
                GameVersion = gameVersion,
                UseLocalBackend = false,
                LocalEndpoint = remoteEndpoint,
                RemoteEndpoint = remoteEndpoint,
                KeepAlivePingIntervalMs = keepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs
            };

            PlayServ.Config(settings);
            Debug.Log(
                $"[PlayServ][Sample] Configured. endpoint={settings.Endpoint}, pingInterval={settings.KeepAlivePingIntervalMs}ms, pongTimeout={settings.KeepAlivePongTimeoutMs}ms");
        }

        [ContextMenu("Connect SDK")]
        public void Connect()
        {
            _ = ConnectAsync();
        }

        [ContextMenu("Disconnect SDK")]
        public void Disconnect()
        {
            PlayServ.Disconnect();
            Debug.Log("[PlayServ][Sample] Disconnected.");
        }

        public async Task ConnectAsync()
        {
            try
            {
                bool connected = await PlayServ.Connect();
                Debug.Log(connected
                    ? "[PlayServ][Sample] Connected."
                    : "[PlayServ][Sample] Connection failed.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayServ][Sample] Connect error: {ex.Message}");
            }
        }

        private void OnTransportError(TransportError error)
        {
            Debug.LogError($"[PlayServ][Sample] Transport error: {error}");
        }
    }
}
