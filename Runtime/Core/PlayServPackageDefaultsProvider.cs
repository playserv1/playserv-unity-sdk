#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// Loads baked package defaults from Resources/PlayServPackageDefaults when available.
    /// </summary>
    public static class PlayServPackageDefaultsProvider
    {
        public const string ResourceName = "PlayServPackageDefaults";

#if UNITY_5_3_OR_NEWER
        public static bool TryLoadAsset(out PlayServPackageDefaults packageDefaults)
        {
            packageDefaults = Resources.Load<PlayServPackageDefaults>(ResourceName);
            return packageDefaults != null;
        }
#endif

        public static bool TryLoadSettings(out PlayServSettings settings)
        {
#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var packageDefaults))
            {
                settings = packageDefaults.ToSettings();
                return true;
            }
#endif

            settings = null;
            return false;
        }

        public static PlayServSettings LoadSettingsOrDefault()
        {
            return TryLoadSettings(out var settings) ? settings : new PlayServSettings();
        }

        public static string ResolveBackendServerAddress(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.BackendServerAddress))
            {
                return packageDefaults.BackendServerAddress.Trim();
            }
#endif

            return PlayServSettings.DefaultBackendServerAddress;
        }

        public static string ResolveDeployApiServerAddress(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.DeployApiServerAddress))
            {
                return packageDefaults.DeployApiServerAddress.Trim();
            }
#endif

            return PlayServSettings.DefaultDeployApiServerAddress;
        }

        public static string ResolveSchemaApiServerAddress(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.SchemaApiServerAddress))
            {
                return packageDefaults.SchemaApiServerAddress.Trim();
            }
#endif

            return PlayServSettings.DefaultSchemaApiServerAddress;
        }

        public static string ResolveDashboardAddress(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadAsset(out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.DashboardAddress))
            {
                return packageDefaults.DashboardAddress.Trim();
            }
#endif

            return PlayServSettings.DefaultDashboardAddress;
        }
    }
}
