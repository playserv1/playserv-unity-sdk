using System;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.Serialization;

namespace Playserv.Examples
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

        [Header("Resolved Endpoints (Read Only)")]
        [FormerlySerializedAs("remoteEndpoint")]
        [SerializeField] private string backendServerAddress = PlayServSettings.DefaultBackendServerAddress;
        [SerializeField] private string deployApiServerAddress = PlayServSettings.DefaultDeployApiServerAddress;
        [SerializeField] private string schemaApiServerAddress = PlayServSettings.DefaultSchemaApiServerAddress;

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
            RefreshResolvedEndpointsPreview();
        }

        private void Start()
        {
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

        private void OnValidate()
        {
            RefreshResolvedEndpointsPreview();
        }

        [ContextMenu("Configure SDK")]
        public void Configure()
        {
            var settings = BuildSettingsFromConfig();
            settings.GameAccessToken = gameAccessToken;
            settings.GameId = gameId;
            settings.UserId = userId;
            settings.GameVersion = gameVersion;
            settings.KeepAlivePingIntervalMs = keepAlivePingIntervalMs;
            settings.KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs;

            PlayServ.Config(settings);
            ApplyResolvedEndpointsPreview(settings);
            Debug.Log(
                $"[PlayServ][Sample] Configured. backend={settings.BackendServerAddress}, pingInterval={settings.KeepAlivePingIntervalMs}ms, pongTimeout={settings.KeepAlivePongTimeoutMs}ms");
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

        private void RefreshResolvedEndpointsPreview()
        {
            var settings = BuildSettingsFromConfig();
            ApplyResolvedEndpointsPreview(settings);
        }

        private void ApplyResolvedEndpointsPreview(PlayServSettings settings)
        {
            if (settings == null)
                return;

            backendServerAddress = settings.BackendServerAddress;
            deployApiServerAddress = settings.DeployApiServerAddress;
            schemaApiServerAddress = settings.SchemaApiServerAddress;
        }

        private static PlayServSettings BuildSettingsFromConfig()
        {
            var config = Resources.Load<PlayServConfig>("PlayServConfig");
            if (config == null)
                return new PlayServSettings();

#if UNITY_EDITOR
            return PlayServEnvironmentResolver.ResolveSettingsForEditor(
                config,
                out _,
                out _,
                out _);
#else
            return config.ToSettings();
#endif
        }
    }
}
