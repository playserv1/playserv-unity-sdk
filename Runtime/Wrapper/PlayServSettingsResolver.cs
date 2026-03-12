#nullable enable

using System;
#if UNITY_EDITOR
using System.Reflection;
#endif
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
        private const string ClientBridgeTypeName = "Playserv.ClientEditor.PlayServProjectSettingsBridge";
        private const string TryResolveSettingsMethodName = "TryResolveSettings";

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

            var bridgeType = FindClientBridgeType();
            if (bridgeType == null)
                return false;

            var method = bridgeType.GetMethod(
                TryResolveSettingsMethodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (method == null)
                return false;

            try
            {
                var args = new object[] { config, settings };
                var result = method.Invoke(null, args);
                if (args[1] is PlayServSettings resolvedSettings)
                    settings = resolvedSettings;

                return result is bool applied && applied;
            }
            catch
            {
                settings = config.ToSettings();
                return false;
            }
        }

        public static bool IsClientProjectContext()
        {
            return FindClientBridgeType() != null;
        }

        private static Type? FindClientBridgeType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(ClientBridgeTypeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
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

            settings = null!;
            return false;
        }

        private static PlayServSettings MergeWithPackageDefaults(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!PlayServPackageDefaultsProvider.TryLoadSettings(out var packageDefaults))
                return settings;

            var merged = settings.Clone();
            if (string.IsNullOrWhiteSpace(merged.GameAccessToken))
                merged.GameAccessToken = packageDefaults.GameAccessToken;
            if (string.IsNullOrWhiteSpace(merged.GameId))
                merged.GameId = packageDefaults.GameId;

            merged.BackendServerAddress = packageDefaults.BackendServerAddress;
            merged.DeployApiServerAddress = packageDefaults.DeployApiServerAddress;
            merged.SchemaApiServerAddress = packageDefaults.SchemaApiServerAddress;

            return merged;
        }
    }
}

#nullable restore
