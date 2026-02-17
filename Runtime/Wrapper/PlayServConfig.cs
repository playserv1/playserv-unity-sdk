#if UNITY_5_3_OR_NEWER
using Playserv.Proxy.Common;
using UnityEngine;
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
        private const string DEFAULT_LOCAL_ENDPOINT = "ws://localhost:8080/ws/";
        private const string DEFAULT_REMOTE_ENDPOINT = "wss://playserv-proxy.test.playserv.io/ws";

        [SerializeField] private string gameAccessToken;
        [SerializeField] private string gameId;
        [SerializeField] private string userId;
        [SerializeField] private string gameVersion = "1.0.0";
        [SerializeField] private string sdkVersion = SdkInfo.Version;
        [SerializeField] private bool allowMultipleConnections = true;
        [SerializeField] private int keepAlivePingIntervalMs = 30000;
        [SerializeField] private int keepAlivePongTimeoutMs = 10000;
        [SerializeField] private int networkTransformSyncIntervalMs = 100;
        [SerializeField] private bool useLocalBackend = false;
        [SerializeField] private string localEndpoint = DEFAULT_LOCAL_ENDPOINT;
        [SerializeField] private string remoteEndpoint = DEFAULT_REMOTE_ENDPOINT;
        
        
        [Header("Deploy")]
        [SerializeField] private string deployApiEndpoint = "https://playserv-backoffice.test.playserv.io/api/deployments";
        [SerializeField] private string deployAuthToken = "";
        [SerializeField] private int timeoutSeconds = 120;

        /// <summary>
        /// Game access token used for handshake.
        /// </summary>
        public string GameAccessToken => gameAccessToken;

        /// <summary>
        /// Game identifier.
        /// </summary>
        public string GameId => gameId;

        /// <summary>
        /// User/player identifier.
        /// </summary>
        public string UserId
        {
            get => userId;
            set => userId = value;
        }

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
        /// Chooses local endpoint when true, remote endpoint otherwise.
        /// </summary>
        public bool UseLocalBackend => useLocalBackend;

        /// <summary>
        /// Local websocket endpoint.
        /// </summary>
        public string LocalEndpoint => localEndpoint;

        /// <summary>
        /// Remote websocket endpoint.
        /// </summary>
        public string RemoteEndpoint => remoteEndpoint;

        /// <summary>
        /// Active endpoint based on <see cref="UseLocalBackend"/>.
        /// </summary>
        public string Endpoint => useLocalBackend ? localEndpoint : remoteEndpoint;
        
        /// <summary>
        /// Deployment API endpoint used by editor deployment tools.
        /// </summary>
        public string DeployApiEndpoint => deployApiEndpoint;

        /// <summary>
        /// Optional bearer token used by editor deployment HTTP requests.
        /// </summary>
        public string DeployAuthToken => deployAuthToken;

        /// <summary>
        /// Timeout in seconds for deployment HTTP requests.
        /// </summary>
        public int TimeoutSeconds => timeoutSeconds;
        
        internal PlayServSettings ToSettings()
        {
            return new PlayServSettings
            {
                GameAccessToken = gameAccessToken,
                GameId = gameId,
                UserId = userId,
                GameVersion = gameVersion,
                SdkVersion = sdkVersion,
                AllowMultipleConnections = allowMultipleConnections,
                KeepAlivePingIntervalMs = keepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs,
                NetworkTransformSyncIntervalMs = networkTransformSyncIntervalMs,
                UseLocalBackend = useLocalBackend,
                LocalEndpoint = localEndpoint,
                RemoteEndpoint = remoteEndpoint,
                DeployApiEndpoint = deployApiEndpoint,
                DeployAuthToken = deployAuthToken,
                TimeoutSeconds = timeoutSeconds
            };
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
        /// Updates game access token and marks asset dirty in editor.
        /// </summary>
        /// <param name="value">New token value.</param>
        public void SetGameAccessToken(string value)
        {
            gameAccessToken = value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

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
    }
}
#endif
