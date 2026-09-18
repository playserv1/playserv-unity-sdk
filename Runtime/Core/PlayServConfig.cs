#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using Playserv.Proxy.Common;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// ScriptableObject configuration stored in Resources and used by PlayServ runtime/editor tooling.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayServConfig", menuName = "PlayServ/Config", order = 0)]
    public sealed class PlayServConfig : ScriptableObject
    {
#if UNITY_EDITOR
        internal interface IEditorClientTokens
        {
            bool TryGet(PlayServConfig config, out string value);
            bool TrySet(PlayServConfig config, string value, out bool changed);
        }

        internal static IEditorClientTokens EditorClientTokens;
        internal string SerializedClientToken => clientToken;

        internal void SetSerializedClientToken(string value)
        {
            if (AssignIfDifferent(ref clientToken, value))
                EditorUtility.SetDirty(this);
        }
#endif
        private const string DEFAULT_BACKEND_SERVER_ADDRESS = PlayServSettings.DefaultBackendServerAddress;
        private const string DEFAULT_DEPLOY_API_SERVER_ADDRESS = PlayServSettings.DefaultDeployApiServerAddress;
        private const string DEFAULT_SCHEMA_API_SERVER_ADDRESS = PlayServSettings.DefaultSchemaApiServerAddress;
        private const string DEFAULT_DASHBOARD_ADDRESS = PlayServSettings.DefaultDashboardAddress;

        [FormerlySerializedAs("gameAccessToken")]
        [SerializeField] private string clientToken;
#if UNITY_EDITOR
        [FormerlySerializedAs("authorization")]
        [SerializeField, HideInInspector] private string legacyAuthorizationForMigration;
#endif
        [FormerlySerializedAs("gameId")]
        [SerializeField] private string deploymentGameId;
        [SerializeField] private string gameVersion = "1.0.0";
        [SerializeField] private string sdkVersion = SdkInfo.Version;
        [SerializeField] private bool allowMultipleConnections = true;
        [SerializeField] private int keepAlivePingIntervalMs = 30000;
        [SerializeField] private int keepAlivePongTimeoutMs = 10000;
        [SerializeField] private int networkTransformSyncIntervalMs = 100;
        [FormerlySerializedAs("remoteEndpoint")]
        [SerializeField] private string backendServerAddress = DEFAULT_BACKEND_SERVER_ADDRESS;
        [Header("WebRTC")]
        [SerializeField] private string webRtcSignalingServerAddress = PlayServSettings.DefaultWebRtcSignalingServerAddress;
        [SerializeField] private string webRtcDataChannelLabel = PlayServSettings.DefaultWebRtcDataChannelLabel;
        [SerializeField] private string[] webRtcIceServers = Array.Empty<string>();
        
        [FormerlySerializedAs("deployApiEndpoint")]
        [Header("Deploy")]
        [SerializeField] private string deployApiServerAddress = DEFAULT_DEPLOY_API_SERVER_ADDRESS;
        [SerializeField] private string schemaApiServerAddress = DEFAULT_SCHEMA_API_SERVER_ADDRESS;
        [SerializeField] private string dashboardAddress = DEFAULT_DASHBOARD_ADDRESS;
#if UNITY_EDITOR
        [FormerlySerializedAs("deployAuthToken")]
        [SerializeField, HideInInspector] private string legacyDeployAuthTokenForMigration;
#endif
        [SerializeField] private int timeoutSeconds = 120;
        
        /// <summary>
        /// Optional public runtime client token (<c>pk_*</c>) used by DataFlow/runtime-auth handshake.
        /// Managed Editor assets resolve the selected environment's local token; player builds use the baked value.
        /// </summary>
        public string ClientToken
        {
            get
            {
#if UNITY_EDITOR
                if (EditorClientTokens != null && EditorClientTokens.TryGet(this, out var value))
                    return value;
#endif
                return clientToken;
            }
        }

        /// <summary>
        /// Backward-compatible alias for <see cref="ClientToken"/>.
        /// </summary>
        public string GameAccessToken => ClientToken;

        /// <summary>
        /// Optional deployment identifier. It is not part of runtime admission.
        /// </summary>
        public string DeploymentGameId => deploymentGameId;

        /// <summary>
        /// Game client version.
        /// </summary>
        public string GameVersion => gameVersion;

        /// <summary>
        /// SDK version override used in handshake.
        /// </summary>
        public string SdkVersion => sdkVersion;

        /// <summary>
        /// Indicates whether multiple sessions are allowed for same user.
        /// </summary>
        public bool AllowMultipleConnections => allowMultipleConnections;

        /// <summary>
        /// Keepalive ping interval in milliseconds.
        /// </summary>
        public int KeepAlivePingIntervalMs => keepAlivePingIntervalMs;

        /// <summary>
        /// Keepalive pong timeout in milliseconds.
        /// </summary>
        public int KeepAlivePongTimeoutMs => keepAlivePongTimeoutMs;

        /// <summary>
        /// Network transform synchronization interval in milliseconds.
        /// </summary>
        public int NetworkTransformSyncIntervalMs => networkTransformSyncIntervalMs;

        /// <summary>
        /// Backend transport endpoint.
        /// Supports websocket endpoints, desktop UDP endpoints via <c>udp://host:port</c>,
        /// desktop reliable UDP endpoints via <c>rudp://host:port</c>, and WebRTC routing via <c>webrtc://...</c>
        /// when signaling client factory is configured.
        /// </summary>
        public string BackendServerAddress => backendServerAddress;

        /// <summary>
        /// Optional signaling server address used by WebRTC DataChannel transport.
        /// </summary>
        public string WebRtcSignalingServerAddress => webRtcSignalingServerAddress;

        /// <summary>
        /// WebRTC data channel label to negotiate with backend peer.
        /// </summary>
        public string WebRtcDataChannelLabel => webRtcDataChannelLabel;

        /// <summary>
        /// Optional STUN/TURN server list used by WebRTC peer connection.
        /// </summary>
        public string[] WebRtcIceServers => webRtcIceServers == null ? Array.Empty<string>() : (string[])webRtcIceServers.Clone();

        /// <summary>
        /// Backward-compatible alias for <see cref="BackendServerAddress"/>.
        /// </summary>
        public string Endpoint => backendServerAddress;
        
        /// <summary>
        /// Deployment API endpoint used by editor deployment tools.
        /// </summary>
        public string DeployApiServerAddress => deployApiServerAddress;

        /// <summary>
        /// Schema API endpoint used by editor schema tools.
        /// </summary>
        public string SchemaApiServerAddress => schemaApiServerAddress;

        /// <summary>
        /// Dashboard URL used by editor shortcuts.
        /// </summary>
        public string DashboardAddress => dashboardAddress;

        /// <summary>
        /// Timeout in seconds for deployment HTTP requests.
        /// </summary>
        public int TimeoutSeconds => timeoutSeconds;

        /// <summary>Creates a settings snapshot, including the effective local environment token in the Editor.</summary>
        public PlayServSettings ToSettings()
        {
            return new PlayServSettings
            {
                ClientToken = ClientToken,
                DeploymentGameId = deploymentGameId,
                GameVersion = gameVersion,
                SdkVersion = sdkVersion,
                AllowMultipleConnections = allowMultipleConnections,
                KeepAlivePingIntervalMs = keepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs,
                NetworkTransformSyncIntervalMs = networkTransformSyncIntervalMs,
                BackendServerAddress = backendServerAddress,
                WebRtcSignalingServerAddress = webRtcSignalingServerAddress,
                WebRtcDataChannelLabel = webRtcDataChannelLabel,
                WebRtcIceServers = webRtcIceServers == null ? Array.Empty<string>() : (string[])webRtcIceServers.Clone(),
                DeployApiServerAddress = deployApiServerAddress,
                SchemaApiServerAddress = schemaApiServerAddress,
                DashboardAddress = dashboardAddress,
                TimeoutSeconds = timeoutSeconds
            };
        }

        /// <summary>
        /// Applies provided settings to this config asset and marks it dirty in editor if changed.
        /// For managed Editor assets the token is stored locally, not serialized; process token overrides are read-only.
        /// </summary>
        public bool ApplySettings(PlayServSettings settings)
        {
            if (settings == null)
                return false;

            var tokenChanged = AssignClientToken(settings.ClientToken, out var serializedTokenChanged);
            var changed = serializedTokenChanged;
            changed |= AssignIfDifferent(ref deploymentGameId, settings.DeploymentGameId);
            changed |= AssignIfDifferent(ref gameVersion, settings.GameVersion);
            changed |= AssignIfDifferent(ref sdkVersion, settings.SdkVersion);
            changed |= AssignIfDifferent(ref allowMultipleConnections, settings.AllowMultipleConnections);
            changed |= AssignIfDifferent(ref keepAlivePingIntervalMs, settings.KeepAlivePingIntervalMs);
            changed |= AssignIfDifferent(ref keepAlivePongTimeoutMs, settings.KeepAlivePongTimeoutMs);
            changed |= AssignIfDifferent(ref networkTransformSyncIntervalMs, settings.NetworkTransformSyncIntervalMs);
            changed |= AssignIfDifferent(ref backendServerAddress, settings.BackendServerAddress);
            changed |= AssignIfDifferent(ref webRtcSignalingServerAddress, settings.WebRtcSignalingServerAddress);
            changed |= AssignIfDifferent(ref webRtcDataChannelLabel, settings.WebRtcDataChannelLabel);
            changed |= AssignArrayIfDifferent(ref webRtcIceServers, settings.WebRtcIceServers);
            changed |= AssignIfDifferent(ref deployApiServerAddress, settings.DeployApiServerAddress);
            changed |= AssignIfDifferent(ref schemaApiServerAddress, settings.SchemaApiServerAddress);
            changed |= AssignIfDifferent(ref dashboardAddress, settings.DashboardAddress);
            changed |= AssignIfDifferent(ref timeoutSeconds, settings.TimeoutSeconds);

#if UNITY_EDITOR
            if (changed)
                EditorUtility.SetDirty(this);
#endif

            return changed || tokenChanged;
        }

        /// <summary>
        /// Updates allow-multiple-connections flag and marks asset dirty in editor.
        /// </summary>
        /// <param name="value">New value.</param>
        public void SetAllowMultipleConnections(bool value)
        {
            allowMultipleConnections = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Updates the active local environment token for managed Editor assets.
        /// Other configs retain serialized storage; process token overrides are read-only.
        /// </summary>
        /// <param name="value">New token value.</param>
        public void SetClientToken(string value)
        {
            AssignClientToken(value, out var serializedChanged);
#if UNITY_EDITOR
            if (serializedChanged)
                EditorUtility.SetDirty(this);
#endif
        }

        private bool AssignClientToken(string value, out bool serializedChanged)
        {
            serializedChanged = false;
#if UNITY_EDITOR
            if (EditorClientTokens != null && EditorClientTokens.TrySet(this, value, out var changed))
                return changed;
#endif
            serializedChanged = AssignIfDifferent(ref clientToken, value);
            return serializedChanged;
        }

        /// <summary>
        /// Backward-compatible setter for <see cref="ClientToken"/>.
        /// </summary>
        /// <param name="value">New token value.</param>
        public void SetGameAccessToken(string value) => SetClientToken(value);

        /// <summary>
        /// Updates game version and marks asset dirty in editor.
        /// </summary>
        /// <param name="value">New game version.</param>
        public void SetGameVersion(string value)
        {
            gameVersion = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Updates SDK version override and marks asset dirty in editor.
        /// </summary>
        /// <param name="value">New SDK version.</param>
        public void SetSdkVersion(string value)
        {
            sdkVersion = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        internal bool ConsumeLegacySecrets(
            out string authorization,
            out string deployAuthToken)
        {
            authorization = legacyAuthorizationForMigration;
            deployAuthToken = legacyDeployAuthTokenForMigration;

            if (string.IsNullOrEmpty(authorization) && string.IsNullOrEmpty(deployAuthToken))
                return false;

            legacyAuthorizationForMigration = string.Empty;
            legacyDeployAuthTokenForMigration = string.Empty;
            EditorUtility.SetDirty(this);
            return true;
        }

        internal bool HasLegacySecrets =>
            !string.IsNullOrWhiteSpace(legacyAuthorizationForMigration) ||
            !string.IsNullOrWhiteSpace(legacyDeployAuthTokenForMigration);
#endif

        private static bool AssignIfDifferent<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            return true;
        }

        private static bool AssignArrayIfDifferent(ref string[] field, string[] value)
        {
            var normalizedField = field ?? Array.Empty<string>();
            var normalizedValue = value ?? Array.Empty<string>();

            if (normalizedField.Length == normalizedValue.Length)
            {
                var isEqual = true;
                for (var i = 0; i < normalizedField.Length; i++)
                {
                    if (!string.Equals(normalizedField[i], normalizedValue[i], StringComparison.Ordinal))
                    {
                        isEqual = false;
                        break;
                    }
                }

                if (isEqual)
                    return false;
            }

            field = normalizedValue.Length == 0 ? Array.Empty<string>() : (string[])normalizedValue.Clone();
            return true;
        }
    }
}
#endif
