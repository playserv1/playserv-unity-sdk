using System;
using Playserv.Identity;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Mutable runtime configuration used by <see cref="PlayServ.Config(PlayServSettings)"/>.
    /// </summary>
    public sealed class PlayServSettings
    {
        /// <summary>
        /// Default backend transport endpoint.
        /// </summary>
        public const string DefaultBackendServerAddress = "";

        /// <summary>
        /// Default deployment API server address.
        /// </summary>
        public const string DefaultDeployApiServerAddress = "";

        /// <summary>
        /// Default WebRTC signaling server address.
        /// </summary>
        public const string DefaultWebRtcSignalingServerAddress = "";

        /// <summary>
        /// Default WebRTC data channel label.
        /// </summary>
        public const string DefaultWebRtcDataChannelLabel = "playserv";

        /// <summary>
        /// Default schema API server address.
        /// </summary>
        public const string DefaultSchemaApiServerAddress = "";

        /// <summary>
        /// Default dashboard address.
        /// </summary>
        public const string DefaultDashboardAddress = "";

        /// <summary>
        /// Optional public runtime client token (<c>pk_*</c>) used by DataFlow/runtime-auth handshake.
        /// </summary>
        public string ClientToken { get; set; } = string.Empty;

        /// <summary>
        /// Runtime-only player JWT source. The provider is queried before connect and reconnect.
        /// </summary>
        public IPlayServRuntimeTokenProvider RuntimeTokenProvider { get; set; }

        /// <summary>
        /// Optional runtime-only player JWT used for the initial handshake. Prefer
        /// <see cref="RuntimeTokenProvider"/> when the credential can rotate.
        /// This value is never read from or written to <see cref="PlayServConfig"/>.
        /// </summary>
        public string PlayerAccessToken { get; set; } = string.Empty;

        /// <summary>
        /// When enabled, a public <see cref="ClientToken"/> automatically creates and refreshes
        /// an anonymous PlayServ player session unless an explicit runtime token provider or
        /// player access token is supplied.
        /// </summary>
        public bool EnableAutomaticPlayerAuthentication { get; set; } = true;

        /// <summary>
        /// Optional runtime-only persistence used by automatic player authentication.
        /// When omitted, the SDK uses a PlayerPrefs-backed store.
        /// </summary>
        public IPlayServPlayerSessionStore PlayerSessionStore { get; set; }

        /// <summary>
        /// Enables the built-in Android/iOS device fingerprint provider when no explicit
        /// <see cref="PlayerFingerprintProvider"/> is configured. Unsupported platforms omit
        /// the fingerprint and emit one development-only warning.
        /// </summary>
        public bool EnableAutomaticPlayerFingerprint { get; set; } = true;

        /// <summary>
        /// Optional, runtime-only source of consented device signals used for anonymous and
        /// provider login. An explicit provider takes precedence over automatic collection,
        /// including when it returns <see langword="null"/>.
        /// </summary>
        public IPlayServPlayerFingerprintProvider PlayerFingerprintProvider { get; set; }

        /// <summary>
        /// Backward-compatible alias for <see cref="ClientToken"/>.
        /// </summary>
        public string GameAccessToken
        {
            get => ClientToken;
            set => ClientToken = value;
        }

        /// <summary>
        /// Optional legacy deployment identifier used only by deployment-version tooling.
        /// It is never sent in the runtime handshake.
        /// </summary>
        public string DeploymentGameId { get; set; } = string.Empty;

        internal string PlayerId { get; set; } = string.Empty;

        /// <summary>
        /// Current game client version.
        /// </summary>
        public string GameVersion { get; set; } = "1.0.0";

        /// <summary>
        /// When enabled, <see cref="PlayServ.Connect"/> resolves the latest deployed game
        /// version before opening the runtime connection. Disable this for session-only
        /// debug overrides that must use <see cref="GameVersion"/> exactly as provided.
        /// </summary>
        public bool ResolveLatestGameVersionOnConnect { get; set; } = true;

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
        /// Backend transport endpoint. Supports websocket endpoints, desktop UDP endpoints via <c>udp://host:port</c>,
        /// desktop reliable UDP endpoints via <c>rudp://host:port</c>, and WebRTC routing via <c>webrtc://...</c>
        /// when signaling client factory is configured.
        /// </summary>
        public string BackendServerAddress { get; set; } = DefaultBackendServerAddress;

        /// <summary>
        /// Optional signaling server address used by WebRTC DataChannel transport.
        /// </summary>
        public string WebRtcSignalingServerAddress { get; set; } = DefaultWebRtcSignalingServerAddress;

        /// <summary>
        /// WebRTC data channel label to negotiate with backend peer.
        /// </summary>
        public string WebRtcDataChannelLabel { get; set; } = DefaultWebRtcDataChannelLabel;

        /// <summary>
        /// Optional STUN/TURN server list used by WebRTC peer connection.
        /// </summary>
        public string[] WebRtcIceServers { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Deployment API server address used by editor deployment tools.
        /// </summary>
        public string DeployApiServerAddress { get; set; } = DefaultDeployApiServerAddress;

        /// <summary>
        /// Schema API server address used by editor schema tools.
        /// </summary>
        public string SchemaApiServerAddress { get; set; } = DefaultSchemaApiServerAddress;

        /// <summary>
        /// Dashboard URL used by editor shortcuts.
        /// </summary>
        public string DashboardAddress { get; set; } = DefaultDashboardAddress;

        /// <summary>
        /// Backward-compatible alias for <see cref="DeployApiServerAddress"/>.
        /// </summary>
        public string DeployApiEndpoint
        {
            get => DeployApiServerAddress;
            set => DeployApiServerAddress = value;
        }

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
                ClientToken = ClientToken,
                RuntimeTokenProvider = RuntimeTokenProvider,
                PlayerAccessToken = PlayerAccessToken,
                EnableAutomaticPlayerAuthentication = EnableAutomaticPlayerAuthentication,
                PlayerSessionStore = PlayerSessionStore,
                EnableAutomaticPlayerFingerprint = EnableAutomaticPlayerFingerprint,
                PlayerFingerprintProvider = PlayerFingerprintProvider,
                DeploymentGameId = DeploymentGameId,
                PlayerId = PlayerId,
                GameVersion = GameVersion,
                ResolveLatestGameVersionOnConnect = ResolveLatestGameVersionOnConnect,
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

        public PlayServRuntimeSettings ToRuntimeSettings()
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
