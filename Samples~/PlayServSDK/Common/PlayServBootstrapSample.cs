using System;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.Serialization;

namespace Playserv.Samples
{
    /// <summary>
    /// Persistent bootstrap component for configuring and connecting PlayServ in samples.
    /// </summary>
    public sealed class PlayServBootstrapSample : MonoBehaviour
    {
        private static PlayServBootstrapSample _instance;

        [Header("Credentials")]
        [SerializeField] private string gameAccessToken = "your-token";
        [SerializeField] private string gameId = "game-001";
        [SerializeField] private string userId = "player-001";
        [SerializeField] private string gameVersion = "1.0.0";
        [SerializeField] private bool overrideCredentialsFromInspector;

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
                LogTrace("[PlayServ][Sample] AutoConnect is enabled. Starting ConnectAsync().");
                _ = ConnectAsync();
                return;
            }

            LogTrace(
                $"[PlayServ][Sample] AutoConnect is disabled. SDK is configured only; Connect() was not called. Current state={PlayServ.State}.");
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

            // In Unity Editor (especially with Enter Play Mode options),
            // OnDestroy may run without a reliable OnApplicationQuit signal.
            // Disconnect on owner destroy to avoid background polling/transport leftovers.
            if (_isOwner && disconnectOnDestroy)
                PlayServ.Disconnect();
        }

        private void OnValidate()
        {
            RefreshResolvedEndpointsPreview();
        }

        [ContextMenu("Configure SDK")]
        public void Configure()
        {
            var settings = BuildSettingsFromConfig();

            if (overrideCredentialsFromInspector)
            {
                settings.GameAccessToken = gameAccessToken;
                settings.GameId = gameId;
                settings.UserId = userId;
                settings.GameVersion = gameVersion;
            }

            settings.KeepAlivePingIntervalMs = keepAlivePingIntervalMs;
            settings.KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs;

            PlayServ.Config(settings);
            ApplyResolvedEndpointsPreview(settings);
            Debug.Log(
                $"[PlayServ][Sample] Configured. gameId={settings.GameId}, credentialsSource={(overrideCredentialsFromInspector ? "inspector" : "config")}, backend={settings.BackendServerAddress}, pingInterval={settings.KeepAlivePingIntervalMs}ms, pongTimeout={settings.KeepAlivePongTimeoutMs}ms");
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
                return PlayServPackageDefaultsProvider.LoadSettingsOrDefault();

#if UNITY_EDITOR
            return PlayServSettingsResolver.ResolveEditorSettings(config);
#else
            return config.ToSettings();
#endif
        }

        [System.Diagnostics.Conditional("PlayServ_Logs")]
        private static void LogTrace(string message)
        {
            Debug.Log(message);
        }
    }
}
