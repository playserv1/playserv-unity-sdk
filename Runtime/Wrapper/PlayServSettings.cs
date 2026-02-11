using Playserv.Proxy.Common;

#nullable enable

namespace Playserv.Wrapper
{
    public sealed class PlayServSettings
    {
        public const string DefaultLocalEndpoint = "ws://localhost:8080/ws/";
        public const string DefaultRemoteEndpoint = "wss://playserv-proxy.test.playserv.io/ws";

        public string GameAccessToken { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string GameVersion { get; set; } = "1.0.0";
        public string SdkVersion { get; set; } = SdkInfo.Version;
        public bool AllowMultipleConnections { get; set; } = true;
        public int KeepAlivePingIntervalMs { get; set; } = 30000;
        public int KeepAlivePongTimeoutMs { get; set; } = 10000;
        public int NetworkTransformSyncIntervalMs { get; set; } = 100;
        public bool UseLocalBackend { get; set; }
        public string LocalEndpoint { get; set; } = DefaultLocalEndpoint;
        public string RemoteEndpoint { get; set; } = DefaultRemoteEndpoint;
        public string DeployApiEndpoint { get; set; } = "http://localhost:5000/api/deployments";
        public int TimeoutSeconds { get; set; } = 120;

        public string Endpoint => UseLocalBackend ? LocalEndpoint : RemoteEndpoint;

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
