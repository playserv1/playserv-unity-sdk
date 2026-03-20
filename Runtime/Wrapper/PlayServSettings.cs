#nullable enable

namespace Playserv.Wrapper
{
    /// <summary>
    /// Mutable runtime configuration used by <see cref="PlayServ.Config(PlayServSettings)"/>.
    /// </summary>
    public sealed class PlayServSettings
    {
        /// <summary>
        /// Default backend websocket endpoint.
        /// </summary>
        public const string DefaultBackendServerAddress = "";

        /// <summary>
        /// Default deployment API server address.
        /// </summary>
        public const string DefaultDeployApiServerAddress = "";

        /// <summary>
        /// Default schema API server address.
        /// </summary>
        public const string DefaultSchemaApiServerAddress = "";

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
        public string SdkVersion { get; set; } = "0.1.0";

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
        /// Backend websocket endpoint.
        /// </summary>
        public string BackendServerAddress { get; set; } = DefaultBackendServerAddress;

        /// <summary>
        /// Deployment API server address used by editor deployment tools.
        /// </summary>
        public string DeployApiServerAddress { get; set; } = DefaultDeployApiServerAddress;

        /// <summary>
        /// Schema API server address used by editor schema tools.
        /// </summary>
        public string SchemaApiServerAddress { get; set; } = DefaultSchemaApiServerAddress;

        /// <summary>
        /// Backward-compatible alias for <see cref="DeployApiServerAddress"/>.
        /// </summary>
        public string DeployApiEndpoint
        {
            get => DeployApiServerAddress;
            set => DeployApiServerAddress = value;
        }

        /// <summary>
        /// Optional bearer token used by editor deployment HTTP requests.
        /// </summary>
        public string DeployAuthToken { get; set; } = string.Empty;

        /// <summary>
        /// Timeout in seconds for deployment API requests.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Backward-compatible alias for <see cref="BackendServerAddress"/>.
        /// </summary>
        public string Endpoint => BackendServerAddress;

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
                BackendServerAddress = BackendServerAddress,
                DeployApiServerAddress = DeployApiServerAddress,
                SchemaApiServerAddress = SchemaApiServerAddress,
                DeployAuthToken = DeployAuthToken,
                TimeoutSeconds = TimeoutSeconds
            };
        }
    }
}

#nullable restore
