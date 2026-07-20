#if UNITY_5_3_OR_NEWER
using System;
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
        public const string EnvironmentResourceFolderName = "PlayServEnvironments";
        public const string DevelopmentEnvironmentName = "Dev";
        public const string ProductionEnvironmentName = "Prod";

#if UNITY_5_3_OR_NEWER
        public static bool TryLoadAsset(out PlayServPackageDefaults packageDefaults)
        {
            packageDefaults = Resources.Load<PlayServPackageDefaults>(ResourceName);
            return packageDefaults != null;
        }

        public static bool TryLoadEnvironmentAsset(string environmentName, out PlayServPackageDefaults packageDefaults)
        {
            packageDefaults = null;

            var normalizedName = NormalizeEnvironmentName(environmentName);
            if (string.IsNullOrWhiteSpace(normalizedName))
                return TryLoadAsset(out packageDefaults);

            packageDefaults = Resources.Load<PlayServPackageDefaults>(
                $"{EnvironmentResourceFolderName}/{normalizedName}");
            if (packageDefaults != null)
                return true;

            var environmentAssets = Resources.LoadAll<PlayServPackageDefaults>(EnvironmentResourceFolderName);
            foreach (var environmentAsset in environmentAssets)
            {
                if (environmentAsset == null ||
                    !string.Equals(environmentAsset.name, normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                packageDefaults = environmentAsset;
                return true;
            }

            return false;
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

        public static bool TryLoadSettings(string environmentName, out PlayServSettings settings)
        {
#if UNITY_5_3_OR_NEWER
            if (TryLoadEnvironmentAsset(environmentName, out var packageDefaults))
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

        public static PlayServSettings LoadSettingsOrDefault(string environmentName)
        {
            return TryLoadSettings(environmentName, out var settings) ? settings : LoadSettingsOrDefault();
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

        public static string ResolveBackendServerAddress(string value, string environmentName)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadEnvironmentAsset(environmentName, out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.BackendServerAddress))
            {
                return packageDefaults.BackendServerAddress.Trim();
            }
#endif

            return ResolveBackendServerAddress(value);
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

        public static string ResolveDeployApiServerAddress(string value, string environmentName)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadEnvironmentAsset(environmentName, out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.DeployApiServerAddress))
            {
                return packageDefaults.DeployApiServerAddress.Trim();
            }
#endif

            return ResolveDeployApiServerAddress(value);
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

        public static string ResolveSchemaApiServerAddress(string value, string environmentName)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadEnvironmentAsset(environmentName, out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.SchemaApiServerAddress))
            {
                return packageDefaults.SchemaApiServerAddress.Trim();
            }
#endif

            return ResolveSchemaApiServerAddress(value);
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

        public static string ResolveDashboardAddress(string value, string environmentName)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

#if UNITY_5_3_OR_NEWER
            if (TryLoadEnvironmentAsset(environmentName, out var packageDefaults) &&
                !string.IsNullOrWhiteSpace(packageDefaults.DashboardAddress))
            {
                return packageDefaults.DashboardAddress.Trim();
            }
#endif

            return ResolveDashboardAddress(value);
        }

#if UNITY_5_3_OR_NEWER
        private static string NormalizeEnvironmentName(string environmentName)
        {
            if (string.IsNullOrWhiteSpace(environmentName))
                return string.Empty;

            var trimmed = environmentName.Trim();
            if (string.Equals(trimmed, "Development", StringComparison.OrdinalIgnoreCase))
                return DevelopmentEnvironmentName;
            if (string.Equals(trimmed, "Production", StringComparison.OrdinalIgnoreCase))
                return ProductionEnvironmentName;

            return trimmed;
        }
#endif
    }
}
