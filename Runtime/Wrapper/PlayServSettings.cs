using Playserv.Proxy.Common;

#nullable enable

namespace Playserv.Wrapper
{
    /// <summary>
    /// Mutable runtime configuration used by <see cref="PlayServ.Config(PlayServSettings)"/>.
    /// </summary>
    public sealed class PlayServSettings
    {
        /// <summary>
        /// Default local websocket endpoint.
        /// </summary>
        public const string DefaultLocalEndpoint = "ws://localhost:8080/ws/";

        /// <summary>
        /// Default remote websocket endpoint.
        /// </summary>
        public const string DefaultRemoteEndpoint = "wss://playserv-proxy.test.playserv.io/ws";

        /// <summary>
        /// Access token used in handshake.
        /// </summary>
        public string GameAccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Game identifier.
        /// </summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>
        /// Current player/user identifier.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Current game client version.
        /// </summary>
        public string GameVersion { get; set; } = "1.0.0";

        /// <summary>
        /// SDK version sent in handshake.
        /// </summary>
        public string SdkVersion { get; set; } = SdkInfo.Version;

        /// <summary>
        /// Indicates whether server should allow multiple sessions for same user.
        /// </summary>
        public bool AllowMultipleConnections { get; set; } = true;

        /// <summary>
        /// Interval between keepalive ping messages, in milliseconds.
        /// </summary>
        public int KeepAlivePingIntervalMs { get; set; } = 30000;

        /// <summary>
        /// Maximum wait time for keepalive pong, in milliseconds.
        /// </summary>
        public int KeepAlivePongTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Transform replication send interval in milliseconds (used by NetworkTransform).
        /// </summary>
        public int NetworkTransformSyncIntervalMs { get; set; } = 100;

        /// <summary>
        /// Chooses local endpoint when true, otherwise remote endpoint.
        /// </summary>
        public bool UseLocalBackend { get; set; }

        /// <summary>
        /// Local backend endpoint.
        /// </summary>
        public string LocalEndpoint { get; set; } = DefaultLocalEndpoint;

        /// <summary>
        /// Remote backend endpoint.
        /// </summary>
        public string RemoteEndpoint { get; set; } = DefaultRemoteEndpoint;

        /// <summary>
        /// Deployment API endpoint used by editor deployment tools.
        /// </summary>
        public string DeployApiEndpoint { get; set; } = "http://localhost:5000/api/deployments";

        /// <summary>
        /// Timeout in seconds for deployment API requests.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Active websocket endpoint resolved from <see cref="UseLocalBackend"/>.
        /// </summary>
        public string Endpoint => UseLocalBackend ? LocalEndpoint : RemoteEndpoint;

        /// <summary>
        /// Creates a deep copy of current settings object.
        /// </summary>
        /// <returns>New settings instance with copied values.</returns>
        public PlayServSettings Clone()
        {
            return new PlayServSettings
            {
                GameAccessToken = GameAccessToken,
                GameId = GameId,
                UserId = UserId,
                GameVersion = GameVersion,
                SdkVersion = SdkVersion,
                AllowMultipleConnections = AllowMultipleConnections,
                KeepAlivePingIntervalMs = KeepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = KeepAlivePongTimeoutMs,
                NetworkTransformSyncIntervalMs = NetworkTransformSyncIntervalMs,
                UseLocalBackend = UseLocalBackend,
                LocalEndpoint = LocalEndpoint,
                RemoteEndpoint = RemoteEndpoint,
                DeployApiEndpoint = DeployApiEndpoint,
                TimeoutSeconds = TimeoutSeconds
            };
        }
    }
}

#nullable restore
