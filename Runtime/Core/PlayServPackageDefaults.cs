#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Baked package defaults that can be shipped with exported SDK artifacts.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayServPackageDefaults", menuName = "PlayServ/Package Defaults", order = 1)]
    public sealed class PlayServPackageDefaults : ScriptableObject
    {
        [FormerlySerializedAs("gameAccessToken")]
        [SerializeField] private string clientToken = string.Empty;
        [SerializeField] private string authorization = string.Empty;
        [SerializeField] private string gameId = string.Empty;
        [SerializeField] private string backendServerAddress = PlayServSettings.DefaultBackendServerAddress;
        [SerializeField] private string webRtcSignalingServerAddress = PlayServSettings.DefaultWebRtcSignalingServerAddress;
        [SerializeField] private string webRtcDataChannelLabel = PlayServSettings.DefaultWebRtcDataChannelLabel;
        [SerializeField] private string[] webRtcIceServers = Array.Empty<string>();
        [SerializeField] private string deployApiServerAddress = PlayServSettings.DefaultDeployApiServerAddress;
        [SerializeField] private string schemaApiServerAddress = PlayServSettings.DefaultSchemaApiServerAddress;
        [SerializeField] private string dashboardAddress = PlayServSettings.DefaultDashboardAddress;
        [SerializeField] private bool allowMultipleConnections = true;
        [SerializeField] private int keepAlivePingIntervalMs = 30000;
        [SerializeField] private int keepAlivePongTimeoutMs = 10000;
        [SerializeField] private int networkTransformSyncIntervalMs = 100;
        [SerializeField] private int timeoutSeconds = 120;

        public string ClientToken => clientToken;
        public string Authorization => authorization;
        public string GameId => gameId;
        public string BackendServerAddress => backendServerAddress;
        public string WebRtcSignalingServerAddress => webRtcSignalingServerAddress;
        public string WebRtcDataChannelLabel => webRtcDataChannelLabel;
        public string[] WebRtcIceServers => webRtcIceServers == null ? Array.Empty<string>() : (string[])webRtcIceServers.Clone();
        public string DeployApiServerAddress => deployApiServerAddress;
        public string SchemaApiServerAddress => schemaApiServerAddress;
        public string DashboardAddress => dashboardAddress;
        public bool AllowMultipleConnections => allowMultipleConnections;
        public int KeepAlivePingIntervalMs => keepAlivePingIntervalMs;
        public int KeepAlivePongTimeoutMs => keepAlivePongTimeoutMs;
        public int NetworkTransformSyncIntervalMs => networkTransformSyncIntervalMs;
        public int TimeoutSeconds => timeoutSeconds;

        public PlayServSettings ToSettings()
        {
            return new PlayServSettings
            {
                ClientToken = ResolveOptionalText(clientToken),
                Authorization = ResolveOptionalText(authorization),
                GameId = ResolveOptionalText(gameId),
                BackendServerAddress = ResolveText(backendServerAddress, PlayServSettings.DefaultBackendServerAddress),
                WebRtcSignalingServerAddress = ResolveText(webRtcSignalingServerAddress, PlayServSettings.DefaultWebRtcSignalingServerAddress),
                WebRtcDataChannelLabel = ResolveText(webRtcDataChannelLabel, PlayServSettings.DefaultWebRtcDataChannelLabel),
                WebRtcIceServers = webRtcIceServers == null ? Array.Empty<string>() : (string[])webRtcIceServers.Clone(),
                DeployApiServerAddress = ResolveText(deployApiServerAddress, PlayServSettings.DefaultDeployApiServerAddress),
                SchemaApiServerAddress = ResolveText(schemaApiServerAddress, PlayServSettings.DefaultSchemaApiServerAddress),
                DashboardAddress = ResolveText(dashboardAddress, PlayServSettings.DefaultDashboardAddress),
                AllowMultipleConnections = allowMultipleConnections,
                KeepAlivePingIntervalMs = keepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs,
                NetworkTransformSyncIntervalMs = networkTransformSyncIntervalMs,
                TimeoutSeconds = timeoutSeconds
            };
        }

        public void ApplyFromSettings(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            clientToken = ResolveOptionalText(settings.ClientToken);
            authorization = ResolveOptionalText(settings.Authorization);
            gameId = ResolveOptionalText(settings.GameId);
            backendServerAddress = ResolveText(settings.BackendServerAddress, PlayServSettings.DefaultBackendServerAddress);
            webRtcSignalingServerAddress = ResolveText(settings.WebRtcSignalingServerAddress, PlayServSettings.DefaultWebRtcSignalingServerAddress);
            webRtcDataChannelLabel = ResolveText(settings.WebRtcDataChannelLabel, PlayServSettings.DefaultWebRtcDataChannelLabel);
            webRtcIceServers = settings.WebRtcIceServers == null ? Array.Empty<string>() : (string[])settings.WebRtcIceServers.Clone();
            deployApiServerAddress = ResolveText(settings.DeployApiServerAddress, PlayServSettings.DefaultDeployApiServerAddress);
            schemaApiServerAddress = ResolveText(settings.SchemaApiServerAddress, PlayServSettings.DefaultSchemaApiServerAddress);
            dashboardAddress = ResolveText(settings.DashboardAddress, PlayServSettings.DefaultDashboardAddress);
            allowMultipleConnections = settings.AllowMultipleConnections;
            keepAlivePingIntervalMs = settings.KeepAlivePingIntervalMs;
            keepAlivePongTimeoutMs = settings.KeepAlivePongTimeoutMs;
            networkTransformSyncIntervalMs = settings.NetworkTransformSyncIntervalMs;
            timeoutSeconds = settings.TimeoutSeconds;
        }

        private static string ResolveText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string ResolveOptionalText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
#endif
