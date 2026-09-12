using System;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.Serialization;

namespace Playserv.DebugTerminal
{
    /// <summary>
    /// Persistent bootstrap component for configuring and connecting PlayServ in samples.
    /// </summary>
    public sealed class PlayServDebugTerminalBootstrap : MonoBehaviour
    {
        private const float PopupMaxWidth = 640f;
        private const float PopupHeight = 360f;
        private const float PopupMargin = 16f;
        private const float PopupPadding = 16f;
        private const float FieldHeight = 32f;
        private const float FieldRowHeight = 42f;

        private static PlayServDebugTerminalBootstrap _instance;

        [Header("Startup Configuration")]
        [SerializeField] private bool showStartupConfigurationPopup = true;

        [Header("Resolved Endpoints (Read Only)")]
        [FormerlySerializedAs("remoteEndpoint")]
        [SerializeField] private string backendServerAddress = PlayServSettings.DefaultBackendServerAddress;
        [SerializeField] private string deployApiServerAddress = PlayServSettings.DefaultDeployApiServerAddress;
        [SerializeField] private string schemaApiServerAddress = PlayServSettings.DefaultSchemaApiServerAddress;

        [Header("Behavior")]
        [SerializeField] private bool autoConnect;
        [SerializeField] private bool disconnectOnDestroy = true;

        private bool _isOwner;
        private bool _isPopupVisible;
        private bool _isPopupConnecting;
        private string _popupError = string.Empty;
        private PlayServSettings _projectDefaults;
        private string _draftClientToken = string.Empty;
        private string _draftDeploymentGameId = string.Empty;
        private string _draftGameVersion = string.Empty;
        private string _draftBackendServerAddress = string.Empty;
        private bool _draftAllowMultipleConnections = true;

        internal static bool IsStartupConfigurationVisible =>
            _instance != null && _instance._isPopupVisible;

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

            _projectDefaults = BuildSettingsFromConfig();
            LoadPopupDefaults(_projectDefaults);
            ApplyResolvedEndpointsPreview(_projectDefaults);
        }

        private void Start()
        {
            if (!_isOwner)
                return;

            if (showStartupConfigurationPopup && !Application.isBatchMode)
            {
                _isPopupVisible = true;
                LogTrace("[PlayServ][Sample] Waiting for startup connection settings.");
                return;
            }

            ConfigureSettings(_projectDefaults, "project");

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

        private void OnGUI()
        {
            if (!_isOwner || !_isPopupVisible)
                return;

            var previousDepth = GUI.depth;
            var previousColor = GUI.color;
            try
            {
                GUI.depth = -1000;

                var overlayStyle = new GUIStyle(GUI.skin.box);
                overlayStyle.normal.background = Texture2D.whiteTexture;
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, overlayStyle);
                GUI.color = previousColor;

                var width = Mathf.Min(PopupMaxWidth, Mathf.Max(320f, Screen.width - PopupMargin * 2f));
                var height = Mathf.Min(PopupHeight, Mathf.Max(320f, Screen.height - PopupMargin * 2f));
                var popupRect = new Rect(
                    (Screen.width - width) * 0.5f,
                    (Screen.height - height) * 0.5f,
                    width,
                    height);
                DrawPopup(popupRect);
            }
            finally
            {
                GUI.depth = previousDepth;
                GUI.color = previousColor;
            }
        }

        private void DrawPopup(Rect popupRect)
        {
            var windowStyle = new GUIStyle(GUI.skin.window);
            var titleStyle = CreateFixedStyle(GUI.skin.label, 18, FontStyle.Bold, TextAnchor.MiddleLeft);
            var labelStyle = CreateFixedStyle(GUI.skin.label, 14, FontStyle.Normal, TextAnchor.MiddleLeft);
            var fieldStyle = CreateFixedStyle(GUI.skin.textField, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
            var toggleStyle = CreateFixedStyle(GUI.skin.toggle, 14, FontStyle.Normal, TextAnchor.MiddleLeft);
            var buttonStyle = CreateFixedStyle(GUI.skin.button, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
            var errorStyle = CreateFixedStyle(GUI.skin.label, 13, FontStyle.Normal, TextAnchor.UpperLeft);
            errorStyle.wordWrap = true;
            errorStyle.normal.textColor = new Color(1f, 0.42f, 0.36f);

            GUI.Box(popupRect, GUIContent.none, windowStyle);

            var contentX = popupRect.x + PopupPadding;
            var contentWidth = popupRect.width - PopupPadding * 2f;
            GUI.Label(
                new Rect(contentX, popupRect.y + 12f, contentWidth, 28f),
                "Connect to PlayServ",
                titleStyle);

            var labelWidth = Mathf.Clamp(popupRect.width * 0.28f, 130f, 170f);
            var inputX = contentX + labelWidth + 12f;
            var inputWidth = Mathf.Max(100f, popupRect.xMax - PopupPadding - inputX);
            var rowY = popupRect.y + 54f;

            using (new GuiEnabledScope(!_isPopupConnecting))
            {
                DrawTextField(
                    "Client Token",
                    ref _draftClientToken,
                    contentX,
                    inputX,
                    rowY,
                    labelWidth,
                    inputWidth,
                    labelStyle,
                    fieldStyle);
                rowY += FieldRowHeight;
                DrawTextField(
                    "Deployment ID (optional)",
                    ref _draftDeploymentGameId,
                    contentX,
                    inputX,
                    rowY,
                    labelWidth,
                    inputWidth,
                    labelStyle,
                    fieldStyle);
                rowY += FieldRowHeight;
                DrawTextField(
                    "Game Version",
                    ref _draftGameVersion,
                    contentX,
                    inputX,
                    rowY,
                    labelWidth,
                    inputWidth,
                    labelStyle,
                    fieldStyle);
                rowY += FieldRowHeight;
                DrawTextField(
                    "Backend Address",
                    ref _draftBackendServerAddress,
                    contentX,
                    inputX,
                    rowY,
                    labelWidth,
                    inputWidth,
                    labelStyle,
                    fieldStyle);
                rowY += FieldRowHeight;
                _draftAllowMultipleConnections = GUI.Toggle(
                    new Rect(inputX, rowY, inputWidth, 28f),
                    _draftAllowMultipleConnections,
                    "Allow Multiple Connections",
                    toggleStyle);
            }

            var buttonRect = new Rect(
                contentX,
                popupRect.yMax - PopupPadding - 36f,
                contentWidth,
                36f);
            var errorRect = new Rect(
                contentX,
                rowY + 32f,
                contentWidth,
                Mathf.Max(0f, buttonRect.y - rowY - 38f));

            if (!string.IsNullOrWhiteSpace(_popupError))
                GUI.Label(errorRect, _popupError, errorStyle);

            using (new GuiEnabledScope(!_isPopupConnecting))
            {
                if (GUI.Button(
                        buttonRect,
                        _isPopupConnecting ? "Connecting..." : "Connect",
                        buttonStyle))
                    _ = ConnectFromPopupAsync();
            }
        }

        private void OnValidate()
        {
            RefreshResolvedEndpointsPreview();
        }

        [ContextMenu("Configure SDK")]
        public void Configure()
        {
            var settings = BuildSettingsFromConfig();
            ConfigureSettings(settings, "project");
        }

        [ContextMenu("Connect SDK")]
        public void Connect()
        {
            if (_isPopupVisible)
            {
                _ = ConnectFromPopupAsync();
                return;
            }

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
                if (!connected)
                {
                    Debug.LogError("[PlayServ][Sample] Connection failed after player authentication.");
                    return;
                }

                var session = PlayServAuth.CurrentSession;
                Debug.Log(
                    $"[PlayServ][Sample] Connected. playerId={session.PlayerId}, session={session.Kind}.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void OnTransportError(TransportError error)
        {
            if (_isPopupVisible)
                _popupError = error.ToString();
            Debug.LogError($"[PlayServ][Sample] Transport error: {error}");
        }

        private async Task ConnectFromPopupAsync()
        {
            if (_isPopupConnecting)
                return;

            if (!TryBuildPopupSettings(out var settings, out var validationError))
            {
                _popupError = validationError;
                return;
            }

            _isPopupConnecting = true;
            _popupError = string.Empty;
            try
            {
                ConfigureSettings(settings, "startup popup");
                var connected = await PlayServ.Connect();
                if (!connected)
                {
                    _popupError = "Connection failed after player authentication.";
                    return;
                }

                var session = PlayServAuth.CurrentSession;
                if (!session.IsLoggedIn)
                {
                    _popupError = "The transport connected without an authenticated player session.";
                    PlayServ.Disconnect();
                    return;
                }

                Debug.Log(
                    $"[PlayServ][Sample] Connected. playerId={session.PlayerId}, session={session.Kind}.");
                _isPopupVisible = false;
            }
            catch (Exception ex)
            {
                _popupError = ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                _isPopupConnecting = false;
            }
        }

        private bool TryBuildPopupSettings(out PlayServSettings settings, out string error)
        {
            settings = null;
            error = string.Empty;

            var clientToken = _draftClientToken?.Trim() ?? string.Empty;
            var deploymentGameId = _draftDeploymentGameId?.Trim() ?? string.Empty;
            var gameVersion = _draftGameVersion?.Trim() ?? string.Empty;
            var backendServerAddress = _draftBackendServerAddress?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(clientToken))
            {
                error = "Client Token is required.";
                return false;
            }

            if (clientToken.IndexOf('\r') >= 0 || clientToken.IndexOf('\n') >= 0)
            {
                error = "Client Token cannot contain line breaks.";
                return false;
            }

            if (!clientToken.StartsWith("pk_", StringComparison.Ordinal))
            {
                error = "Client Token must be a public pk_* key.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                error = "Game Version is required.";
                return false;
            }

            if (!TryValidateBackendAddress(backendServerAddress, out error))
                return false;

            settings = (_projectDefaults ?? BuildSettingsFromConfig()).Clone();
            settings.ClientToken = clientToken;
            settings.DeploymentGameId = deploymentGameId;
            settings.GameVersion = gameVersion;
            settings.ResolveLatestGameVersionOnConnect = false;
            settings.BackendServerAddress = backendServerAddress;
            settings.AllowMultipleConnections = _draftAllowMultipleConnections;
            settings.EnableAutomaticPlayerAuthentication = true;
            settings.RuntimeTokenProvider = null;
            settings.PlayerAccessToken = string.Empty;
            return true;
        }

        private void ConfigureSettings(PlayServSettings settings, string source)
        {
            if (settings == null)
                throw new InvalidOperationException("PlayServ project settings could not be resolved.");

            settings = settings.Clone();
            settings.EnableAutomaticPlayerAuthentication = true;
            PlayServ.Config(settings);
            ApplyResolvedEndpointsPreview(settings);
            Debug.Log(
                $"[PlayServ][Sample] Configured. source={source}, backend={settings.BackendServerAddress}, pingInterval={settings.KeepAlivePingIntervalMs}ms, pongTimeout={settings.KeepAlivePongTimeoutMs}ms");
        }

        private void LoadPopupDefaults(PlayServSettings settings)
        {
            if (settings == null)
                settings = new PlayServSettings();

            _draftClientToken = settings.ClientToken ?? string.Empty;
            _draftDeploymentGameId = settings.DeploymentGameId ?? string.Empty;
            _draftGameVersion = settings.GameVersion ?? string.Empty;
            _draftBackendServerAddress = settings.BackendServerAddress ?? string.Empty;
            _draftAllowMultipleConnections = settings.AllowMultipleConnections;
        }

        private static void DrawTextField(
            string label,
            ref string value,
            float labelX,
            float inputX,
            float y,
            float labelWidth,
            float inputWidth,
            GUIStyle labelStyle,
            GUIStyle fieldStyle)
        {
            GUI.Label(new Rect(labelX, y, labelWidth, FieldHeight), label, labelStyle);
            value = GUI.TextField(
                new Rect(inputX, y, inputWidth, FieldHeight),
                value ?? string.Empty,
                fieldStyle);
        }

        private static bool TryValidateBackendAddress(string value, out string error)
        {
            error = string.Empty;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                error = "Backend Server Address must be an absolute URI.";
                return false;
            }

            switch (uri.Scheme.ToLowerInvariant())
            {
                case "ws":
                case "wss":
                case "http":
                case "https":
                case "udp":
                case "rudp":
                case "webrtc":
                    return true;
                default:
                    error = $"Unsupported backend scheme '{uri.Scheme}'.";
                    return false;
            }
        }

        private static GUIStyle CreateFixedStyle(
            GUIStyle source,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment)
        {
            return new GUIStyle(source)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
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

        private readonly struct GuiEnabledScope : IDisposable
        {
            private readonly bool _previousValue;

            public GuiEnabledScope(bool enabled)
            {
                _previousValue = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose()
            {
                GUI.enabled = _previousValue;
            }
        }
    }
}
