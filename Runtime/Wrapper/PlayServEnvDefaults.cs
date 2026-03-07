#nullable enable

#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Baked environment defaults that can be shipped with exported SDK artifacts.
    /// </summary>
    [CreateAssetMenu(fileName = "EnvDefaults", menuName = "PlayServ/Env Defaults", order = 1)]
    public sealed class PlayServEnvDefaults : ScriptableObject
    {
        [SerializeField] private string environmentName = PlayServEnvironmentResolver.DefaultEnvironment;
        [SerializeField] private string backendServerAddress = PlayServSettings.DefaultBackendServerAddress;
        [SerializeField] private string deployApiServerAddress = PlayServSettings.DefaultDeployApiServerAddress;
        [SerializeField] private string schemaApiServerAddress = PlayServSettings.DefaultSchemaApiServerAddress;
        [SerializeField] private bool allowMultipleConnections = true;
        [SerializeField] private int keepAlivePingIntervalMs = 30000;
        [SerializeField] private int keepAlivePongTimeoutMs = 10000;
        [SerializeField] private int networkTransformSyncIntervalMs = 100;
        [SerializeField] private int timeoutSeconds = 120;

        public string EnvironmentName => environmentName;
        public string BackendServerAddress => backendServerAddress;
        public string DeployApiServerAddress => deployApiServerAddress;
        public string SchemaApiServerAddress => schemaApiServerAddress;
        public bool AllowMultipleConnections => allowMultipleConnections;
        public int KeepAlivePingIntervalMs => keepAlivePingIntervalMs;
        public int KeepAlivePongTimeoutMs => keepAlivePongTimeoutMs;
        public int NetworkTransformSyncIntervalMs => networkTransformSyncIntervalMs;
        public int TimeoutSeconds => timeoutSeconds;

        public PlayServSettings ToSettings()
        {
            return new PlayServSettings
            {
                BackendServerAddress = ResolveText(backendServerAddress, PlayServSettings.DefaultBackendServerAddress),
                DeployApiServerAddress = ResolveText(deployApiServerAddress, PlayServSettings.DefaultDeployApiServerAddress),
                SchemaApiServerAddress = ResolveText(schemaApiServerAddress, PlayServSettings.DefaultSchemaApiServerAddress),
                AllowMultipleConnections = allowMultipleConnections,
                KeepAlivePingIntervalMs = keepAlivePingIntervalMs,
                KeepAlivePongTimeoutMs = keepAlivePongTimeoutMs,
                NetworkTransformSyncIntervalMs = networkTransformSyncIntervalMs,
                TimeoutSeconds = timeoutSeconds
            };
        }

        public void ApplyFromSettings(string selectedEnvironment, PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            environmentName = string.IsNullOrWhiteSpace(selectedEnvironment)
                ? PlayServEnvironmentResolver.DefaultEnvironment
                : selectedEnvironment.Trim().ToLowerInvariant();

            backendServerAddress = ResolveText(settings.BackendServerAddress, PlayServSettings.DefaultBackendServerAddress);
            deployApiServerAddress = ResolveText(settings.DeployApiServerAddress, PlayServSettings.DefaultDeployApiServerAddress);
            schemaApiServerAddress = ResolveText(settings.SchemaApiServerAddress, PlayServSettings.DefaultSchemaApiServerAddress);
            allowMultipleConnections = settings.AllowMultipleConnections;
            keepAlivePingIntervalMs = settings.KeepAlivePingIntervalMs;
            keepAlivePongTimeoutMs = settings.KeepAlivePongTimeoutMs;
            networkTransformSyncIntervalMs = settings.NetworkTransformSyncIntervalMs;
            timeoutSeconds = settings.TimeoutSeconds;
        }

        private static string ResolveText(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
#endif

#nullable restore
