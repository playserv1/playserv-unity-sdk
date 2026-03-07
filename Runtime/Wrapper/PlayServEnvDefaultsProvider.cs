#nullable enable

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// Loads baked environment defaults from Resources/EnvDefaults when available.
    /// </summary>
    public static class PlayServEnvDefaultsProvider
    {
        public const string ResourceName = "EnvDefaults";

#if UNITY_5_3_OR_NEWER
        public static bool TryLoadAsset(out PlayServEnvDefaults envDefaults)
        {
            envDefaults = Resources.Load<PlayServEnvDefaults>(ResourceName);
            return envDefaults != null;
        }
#endif

        public static bool TryLoadSettings(out PlayServSettings settings)
        {
#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var envDefaults))
            {
                settings = envDefaults.ToSettings();
                return true;
            }
#endif

            settings = null!;
            return false;
        }

        public static PlayServSettings LoadSettingsOrDefault()
        {
            return TryLoadSettings(out var settings) ? settings : new PlayServSettings();
        }

        public static string ResolveBackendServerAddress(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var envDefaults) && !string.IsNullOrWhiteSpace(envDefaults.BackendServerAddress))
                return envDefaults.BackendServerAddress.Trim();
#endif

            return PlayServSettings.DefaultBackendServerAddress;
        }

        public static string ResolveDeployApiServerAddress(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var envDefaults) && !string.IsNullOrWhiteSpace(envDefaults.DeployApiServerAddress))
                return envDefaults.DeployApiServerAddress.Trim();
#endif

            return PlayServSettings.DefaultDeployApiServerAddress;
        }

        public static string ResolveSchemaApiServerAddress(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var envDefaults) && !string.IsNullOrWhiteSpace(envDefaults.SchemaApiServerAddress))
                return envDefaults.SchemaApiServerAddress.Trim();
#endif

            return PlayServSettings.DefaultSchemaApiServerAddress;
        }
    }
}

#nullable restore
