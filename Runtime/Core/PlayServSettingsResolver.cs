using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    /// <summary>
    /// Resolves effective SDK settings from project config and baked package defaults.
    /// </summary>
    public static class PlayServSettingsResolver
    {
        private const string ConfigResourceName = "PlayServConfig";

#if UNITY_EDITOR
        public static PlayServSettings ResolveEditorSettings(PlayServConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (TryResolveClientProjectSettings(config, out var settings))
                return settings;

            return MergeWithPackageDefaults(config.ToSettings());
        }

        public static bool TryResolveClientProjectSettings(PlayServConfig config, out PlayServSettings settings)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            settings = config.ToSettings();

            if (!PlayServClientProjectSettingsRegistry.TryResolveSettings(config, out var resolvedSettings) ||
                resolvedSettings == null)
                return false;

            settings = resolvedSettings;
            return true;
        }

        public static bool IsClientProjectContext()
        {
            return PlayServClientProjectSettingsRegistry.HasProvider;
        }
#endif

        public static bool TryLoadSettingsFromResourcesOrPackageDefaults(out PlayServSettings settings)
        {
#if UNITY_5_3_OR_NEWER
            var config = Resources.Load<PlayServConfig>(ConfigResourceName);
            if (config != null)
            {
#if UNITY_EDITOR
                settings = ResolveEditorSettings(config);
#else
                settings = MergeWithPackageDefaults(config.ToSettings());
#endif
                return true;
            }

            if (PlayServPackageDefaultsProvider.TryLoadSettings(out var packageDefaults))
            {
                settings = packageDefaults;
                return true;
            }
#endif

            settings = null;
            return false;
        }

        private static PlayServSettings MergeWithPackageDefaults(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!PlayServPackageDefaultsProvider.TryLoadSettings(out var packageDefaults))
                return settings;

            var merged = settings.Clone();
            if (string.IsNullOrWhiteSpace(merged.ClientToken))
                merged.ClientToken = packageDefaults.ClientToken;
            if (string.IsNullOrWhiteSpace(merged.GameId))
                merged.GameId = packageDefaults.GameId;
            if (string.IsNullOrWhiteSpace(merged.BackendServerAddress))
                merged.BackendServerAddress = packageDefaults.BackendServerAddress;
            if (string.IsNullOrWhiteSpace(merged.WebRtcSignalingServerAddress))
                merged.WebRtcSignalingServerAddress = packageDefaults.WebRtcSignalingServerAddress;
            if (string.IsNullOrWhiteSpace(merged.WebRtcDataChannelLabel))
                merged.WebRtcDataChannelLabel = packageDefaults.WebRtcDataChannelLabel;
            if ((merged.WebRtcIceServers == null || merged.WebRtcIceServers.Length == 0) &&
                packageDefaults.WebRtcIceServers != null)
            {
                merged.WebRtcIceServers = (string[])packageDefaults.WebRtcIceServers.Clone();
            }
            if (string.IsNullOrWhiteSpace(merged.DeployApiServerAddress))
                merged.DeployApiServerAddress = packageDefaults.DeployApiServerAddress;
            if (string.IsNullOrWhiteSpace(merged.SchemaApiServerAddress))
                merged.SchemaApiServerAddress = packageDefaults.SchemaApiServerAddress;
            if (string.IsNullOrWhiteSpace(merged.DashboardAddress))
                merged.DashboardAddress = packageDefaults.DashboardAddress;

            return merged;
        }
    }
}
