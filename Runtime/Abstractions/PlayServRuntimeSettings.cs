using System;

namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Neutral runtime settings DTO consumed by transport/http modules and runtime integrations.
    /// </summary>
    public sealed class PlayServRuntimeSettings
    {
        public const string DefaultBackendServerAddress = "";
        public const string DefaultDeployApiServerAddress = "";
        public const string DefaultWebRtcSignalingServerAddress = "";
        public const string DefaultWebRtcDataChannelLabel = "playserv";
        public const string DefaultSchemaApiServerAddress = "";
        public const string DefaultDashboardAddress = "";

        public string ClientToken { get; set; } = string.Empty;
        public IPlayServRuntimeTokenProvider RuntimeTokenProvider { get; set; }
        public string PlayerAccessToken { get; set; } = string.Empty;

        public string GameAccessToken
        {
            get => ClientToken;
            set => ClientToken = value;
        }

        public string DeploymentGameId { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public string GameVersion { get; set; } = "1.0.0";
        public string SdkVersion { get; set; } = "0.5.1";
        public bool AllowMultipleConnections { get; set; } = true;
        public int KeepAlivePingIntervalMs { get; set; } = 30000;
        public int KeepAlivePongTimeoutMs { get; set; } = 10000;
        public int NetworkTransformSyncIntervalMs { get; set; } = 100;
        public string BackendServerAddress { get; set; } = DefaultBackendServerAddress;
        public string WebRtcSignalingServerAddress { get; set; } = DefaultWebRtcSignalingServerAddress;
        public string WebRtcDataChannelLabel { get; set; } = DefaultWebRtcDataChannelLabel;
        public string[] WebRtcIceServers { get; set; } = Array.Empty<string>();
        public string DeployApiServerAddress { get; set; } = DefaultDeployApiServerAddress;
        public string SchemaApiServerAddress { get; set; } = DefaultSchemaApiServerAddress;
        public string DashboardAddress { get; set; } = DefaultDashboardAddress;
        public int TimeoutSeconds { get; set; } = 120;

        public string Endpoint => BackendServerAddress;

        public PlayServRuntimeSettings Clone()
        {
            return new PlayServRuntimeSettings
            {
                ClientToken = ClientToken,
                RuntimeTokenProvider = RuntimeTokenProvider,
                PlayerAccessToken = PlayerAccessToken,
                DeploymentGameId = DeploymentGameId,
                PlayerId = PlayerId,
                GameVersion = GameVersion,
                SdkVersion = SdkVersion,
                AllowMultipleConnections = AllowMultipleConnections,
                KeepAlivePingIntervalMs = KeepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = KeepAlivePongTimeoutMs,
                NetworkTransformSyncIntervalMs = NetworkTransformSyncIntervalMs,
                BackendServerAddress = BackendServerAddress,
                WebRtcSignalingServerAddress = WebRtcSignalingServerAddress,
                WebRtcDataChannelLabel = WebRtcDataChannelLabel,
                WebRtcIceServers = WebRtcIceServers == null ? Array.Empty<string>() : (string[])WebRtcIceServers.Clone(),
                DeployApiServerAddress = DeployApiServerAddress,
                SchemaApiServerAddress = SchemaApiServerAddress,
                DashboardAddress = DashboardAddress,
                TimeoutSeconds = TimeoutSeconds
            };
        }
    }
}
