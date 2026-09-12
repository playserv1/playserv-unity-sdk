using System;
using System.Collections.Generic;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServPackageEnvironmentProvider
    {
        private const string ThisScriptSuffix = "/Editor/PlayServPackageEnvironmentProvider.cs";
        private const string EnvironmentAssetsRelativePath = "Runtime/Resources/PlayServEnvironments";
        private const string ActiveEnvironmentPrefKey = "PlayServ.PackageEnvironment.Active";

        private static readonly IPlayServClientProjectSettingsProvider Provider = new PackageEnvironmentProvider();
        private static PlayServPackageRoot _packageRoot;

        static PlayServPackageEnvironmentProvider()
        {
            PlayServClientProjectSettingsRegistry.RegisterFallback(Provider);
        }

        private static bool TryResolveSettings(PlayServConfig config, out PlayServSettings settings)
        {
            settings = null;
            if (config == null)
                return false;

            var environments = GetEnvironmentNames();
            var activeEnvironment = ResolveActiveEnvironmentName(environments);
            if (!TryLoadEnvironmentDefaults(activeEnvironment, out var environmentDefaults))
                return false;

            settings = config.ToSettings();
            ApplyMissingPackageDefaults(settings, environmentDefaults.ToSettings());
            return true;
        }

        private static bool DrawProjectConfigUi(out bool changed)
        {
            changed = false;

            var environments = GetEnvironmentNames();
            if (environments.Length == 0)
                return false;

            var activeEnvironment = ResolveActiveEnvironmentName(environments);
            var activeIndex = IndexOf(environments, activeEnvironment);
            if (activeIndex < 0)
                activeIndex = 0;

            var labels = new string[environments.Length];
            for (var i = 0; i < environments.Length; i++)
                labels[i] = AsDisplayEnvironmentLabel(environments[i]);

            var selectedIndex = EditorGUILayout.Popup("Environment", activeIndex, labels);
            if (selectedIndex == activeIndex)
                return true;

            var selectedEnvironment = environments[selectedIndex];
            EditorPrefs.SetString(ActiveEnvironmentPrefKey, selectedEnvironment);
            var config = PlayServConfigProvider.FindExisting();
            if (config != null && TryApplyEnvironmentDefaults(config, selectedEnvironment))
                AssetDatabase.SaveAssets();

            changed = true;
            return true;
        }

        internal static bool TryApplyActiveEnvironmentDefaults(PlayServConfig config)
        {
            if (config == null)
                return false;

            var environments = GetEnvironmentNames();
            var activeEnvironment = ResolveActiveEnvironmentName(environments);
            return TryApplyEnvironmentDefaults(config, activeEnvironment);
        }

        private static bool TryApplyEnvironmentDefaults(
            PlayServConfig config,
            string environmentName)
        {
            if (config == null ||
                !TryLoadEnvironmentDefaults(environmentName, out var environmentDefaults))
            {
                return false;
            }

            var settings = config.ToSettings();
            ApplyEnvironmentDefaults(settings, environmentDefaults.ToSettings());
            return config.ApplySettings(settings);
        }

        private static string[] GetEnvironmentNames()
        {
            var names = new List<string>();
            AddEnvironmentAssetNames(names);
            AddIfMissing(names, PlayServPackageDefaultsProvider.DevelopmentEnvironmentName);
            AddIfMissing(names, PlayServPackageDefaultsProvider.ProductionEnvironmentName);
            names.Sort(CompareEnvironmentNames);
            return names.ToArray();
        }

        private static void AddEnvironmentAssetNames(List<string> names)
        {
            if (!TryGetPackageRoot(out var packageRoot))
                return;

            var folder = CombineAssetPath(packageRoot.AssetPath, EnvironmentAssetsRelativePath);
            try
            {
                var guids = AssetDatabase.FindAssets("t:PlayServPackageDefaults", new[] { folder });
                for (var i = 0; i < guids.Length; i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var asset = AssetDatabase.LoadAssetAtPath<PlayServPackageDefaults>(assetPath);
                    if (asset != null)
                        AddIfMissing(names, asset.name);
                }
            }
            catch
            {
                // Package assets are optional in source layouts; fall back to the built-in names.
            }
        }

        private static bool TryLoadEnvironmentDefaults(string environmentName, out PlayServPackageDefaults defaults)
        {
            defaults = null;
            if (!TryNormalizeEnvironmentName(environmentName, out var normalized))
                return false;

            if (TryGetPackageRoot(out var packageRoot))
            {
                var assetPath = CombineAssetPath(
                    packageRoot.AssetPath,
                    $"{EnvironmentAssetsRelativePath}/{normalized}.asset");
                defaults = AssetDatabase.LoadAssetAtPath<PlayServPackageDefaults>(assetPath);
                if (defaults != null)
                    return true;
            }

            return PlayServPackageDefaultsProvider.TryLoadEnvironmentAsset(normalized, out defaults);
        }

        private static void ApplyEnvironmentDefaults(PlayServSettings target, PlayServSettings defaults)
        {
            if (target == null || defaults == null)
                return;

            AssignIfSet(value => target.ClientToken = value, defaults.ClientToken);
            AssignIfSet(value => target.DeploymentGameId = value, defaults.DeploymentGameId);
            AssignIfSet(value => target.BackendServerAddress = value, defaults.BackendServerAddress);
            AssignIfSet(value => target.WebRtcSignalingServerAddress = value, defaults.WebRtcSignalingServerAddress);
            AssignIfSet(value => target.WebRtcDataChannelLabel = value, defaults.WebRtcDataChannelLabel);
            AssignIfSet(value => target.DeployApiServerAddress = value, defaults.DeployApiServerAddress);
            AssignIfSet(value => target.SchemaApiServerAddress = value, defaults.SchemaApiServerAddress);
            AssignIfSet(value => target.DashboardAddress = value, defaults.DashboardAddress);

            var iceServers = defaults.WebRtcIceServers;
            if (iceServers.Length > 0)
                target.WebRtcIceServers = (string[])iceServers.Clone();

            target.AllowMultipleConnections = defaults.AllowMultipleConnections;
            target.KeepAlivePingIntervalMs = defaults.KeepAlivePingIntervalMs;
            target.KeepAlivePongTimeoutMs = defaults.KeepAlivePongTimeoutMs;
            target.NetworkTransformSyncIntervalMs = defaults.NetworkTransformSyncIntervalMs;
            target.TimeoutSeconds = defaults.TimeoutSeconds;
        }

        private static void ApplyMissingPackageDefaults(
            PlayServSettings target,
            PlayServSettings defaults)
        {
            if (target == null || defaults == null)
                return;

            AssignIfMissing(value => target.ClientToken = value, target.ClientToken, defaults.ClientToken);
            AssignIfMissing(
                value => target.DeploymentGameId = value,
                target.DeploymentGameId,
                defaults.DeploymentGameId);
            AssignIfMissing(
                value => target.BackendServerAddress = value,
                target.BackendServerAddress,
                defaults.BackendServerAddress);
            AssignIfMissing(
                value => target.WebRtcSignalingServerAddress = value,
                target.WebRtcSignalingServerAddress,
                defaults.WebRtcSignalingServerAddress);
            AssignIfMissing(
                value => target.WebRtcDataChannelLabel = value,
                target.WebRtcDataChannelLabel,
                defaults.WebRtcDataChannelLabel);
            AssignIfMissing(
                value => target.DeployApiServerAddress = value,
                target.DeployApiServerAddress,
                defaults.DeployApiServerAddress);
            AssignIfMissing(
                value => target.SchemaApiServerAddress = value,
                target.SchemaApiServerAddress,
                defaults.SchemaApiServerAddress);
            AssignIfMissing(
                value => target.DashboardAddress = value,
                target.DashboardAddress,
                defaults.DashboardAddress);

            if ((target.WebRtcIceServers == null || target.WebRtcIceServers.Length == 0) &&
                defaults.WebRtcIceServers != null &&
                defaults.WebRtcIceServers.Length > 0)
            {
                target.WebRtcIceServers = (string[])defaults.WebRtcIceServers.Clone();
            }
        }

        private static void AssignIfSet(Action<string> assign, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                assign(value.Trim());
        }

        private static void AssignIfMissing(
            Action<string> assign,
            string currentValue,
            string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(currentValue) &&
                !string.IsNullOrWhiteSpace(defaultValue))
            {
                assign(defaultValue.Trim());
            }
        }

        private static string ResolveActiveEnvironmentName(string[] environments)
        {
            var stored = EditorPrefs.GetString(
                ActiveEnvironmentPrefKey,
                PlayServPackageDefaultsProvider.DevelopmentEnvironmentName);

            if (TryNormalizeEnvironmentName(stored, out var normalized) &&
                TryGetExistingEnvironmentName(environments, normalized, out var existing))
            {
                return existing;
            }

            if (TryGetExistingEnvironmentName(
                    environments,
                    PlayServPackageDefaultsProvider.DevelopmentEnvironmentName,
                    out existing))
            {
                return existing;
            }

            return environments.Length > 0 ? environments[0] : PlayServPackageDefaultsProvider.DevelopmentEnvironmentName;
        }

        private static bool TryGetExistingEnvironmentName(
            string[] environments,
            string environmentName,
            out string existing)
        {
            existing = null;
            if (!TryNormalizeEnvironmentName(environmentName, out var normalized))
                return false;

            for (var i = 0; i < environments.Length; i++)
            {
                if (string.Equals(environments[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    existing = environments[i];
                    return true;
                }
            }

            return false;
        }

        private static bool TryNormalizeEnvironmentName(string value, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "local":
                case "localhost":
                    normalized = "Local";
                    return true;
                case "dev":
                case "development":
                    normalized = PlayServPackageDefaultsProvider.DevelopmentEnvironmentName;
                    return true;
                case "test":
                case "testing":
                    normalized = "Test";
                    return true;
                case "prod":
                case "production":
                    normalized = PlayServPackageDefaultsProvider.ProductionEnvironmentName;
                    return true;
                default:
                    normalized = value.Trim();
                    return true;
            }
        }

        private static string AsDisplayEnvironmentLabel(string environmentName)
        {
            return TryNormalizeEnvironmentName(environmentName, out var normalized)
                ? normalized
                : environmentName;
        }

        private static int IndexOf(string[] values, string value)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static int CompareEnvironmentNames(string left, string right)
        {
            var leftOrder = GetEnvironmentOrder(left);
            var rightOrder = GetEnvironmentOrder(right);
            if (leftOrder != rightOrder)
                return leftOrder.CompareTo(rightOrder);

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetEnvironmentOrder(string environmentName)
        {
            if (!TryNormalizeEnvironmentName(environmentName, out var normalized))
                return 100;

            switch (normalized)
            {
                case "Local":
                    return 0;
                case PlayServPackageDefaultsProvider.DevelopmentEnvironmentName:
                    return 1;
                case "Test":
                    return 2;
                case PlayServPackageDefaultsProvider.ProductionEnvironmentName:
                    return 3;
                default:
                    return 100;
            }
        }

        private static void AddIfMissing(List<string> names, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            names.Add(name.Trim());
        }

        private static bool TryGetPackageRoot(out PlayServPackageRoot packageRoot)
        {
            if (_packageRoot != null)
            {
                if (_packageRoot.Exists)
                {
                    packageRoot = _packageRoot;
                    return true;
                }

                _packageRoot = null;
            }

            if (!PlayServPackagePathResolver.TryResolveRootForScript(
                nameof(PlayServPackageEnvironmentProvider),
                ThisScriptSuffix,
                out packageRoot))
            {
                return false;
            }

            _packageRoot = packageRoot;
            return true;
        }

        private static string CombineAssetPath(string rootAssetPath, string relativePath)
        {
            rootAssetPath = NormalizeAssetPath(rootAssetPath);
            relativePath = NormalizeAssetPath(relativePath);
            return string.IsNullOrEmpty(relativePath)
                ? rootAssetPath
                : $"{rootAssetPath}/{relativePath}";
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim('/');
        }

        private sealed class PackageEnvironmentProvider : IPlayServClientProjectSettingsProvider
        {
            public bool TryResolveSettings(PlayServConfig config, out PlayServSettings settings)
            {
                return PlayServPackageEnvironmentProvider.TryResolveSettings(config, out settings);
            }

            public bool TryDrawProjectConfigUi(out bool changed)
            {
                return PlayServPackageEnvironmentProvider.DrawProjectConfigUi(out changed);
            }
        }
    }
}
