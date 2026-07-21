using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal static class PlayServConfigProvider
    {
        // You can change this path if you want a different location.
        private const string AssetPath = "Assets/Resources/PlayServConfig.asset";
        private const string BackendServerAddressPropertyName = "backendServerAddress";
        private const string DeployApiServerAddressPropertyName = "deployApiServerAddress";
        private const string SchemaApiServerAddressPropertyName = "schemaApiServerAddress";
        private const string DashboardAddressPropertyName = "dashboardAddress";
        private const string LegacyDefaultSdkVersion = "1.0.0";

        public static PlayServConfig GetOrCreate()
        {
            var config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(AssetPath);
            if (config != null)
            {
                var changed = EnsureBackendServerAddress(config);
                changed |= EnsureDeployApiServerAddress(config);
                changed |= EnsureSchemaApiServerAddress(config);
                changed |= EnsureDashboardAddress(config);
                changed |= EnsureDefaultSdkVersion(config);
                changed |= ApplyEnvironmentProfile(config);
                if (changed)
                    AssetDatabase.SaveAssets();
                return config;
            }

            // Ensure folder exists
            var folder = System.IO.Path.GetDirectoryName(AssetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                CreateFolders(folder);
            }

            config = ScriptableObject.CreateInstance<PlayServConfig>();
            AssetDatabase.CreateAsset(config, AssetPath);
            EnsureBackendServerAddress(config);
            EnsureDeployApiServerAddress(config);
            EnsureSchemaApiServerAddress(config);
            EnsureDashboardAddress(config);
            EnsureDefaultSdkVersion(config);
            ApplyEnvironmentProfile(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return config;
        }

        public static PlayServConfig FindExisting()
        {
            // Fast-path: direct path
            var atPath = AssetDatabase.LoadAssetAtPath<PlayServConfig>(AssetPath);
            if (atPath != null)
                return atPath;

            // Scan project
            var guids = AssetDatabase.FindAssets("t:PlayServConfig");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<PlayServConfig>(path);
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static bool EnsureBackendServerAddress(PlayServConfig config)
        {
            if (config == null)
                return false;

            var serializedObject = new SerializedObject(config);
            var backendProperty = serializedObject.FindProperty(BackendServerAddressPropertyName);
            if (backendProperty == null)
                return false;

            var currentValue = backendProperty.stringValue?.Trim();
            var shouldReplace = string.IsNullOrWhiteSpace(currentValue);
            if (!shouldReplace)
                return false;

            backendProperty.stringValue = PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return true;
        }

        private static void CreateFolders(string folderPath)
        {
            // folderPath like "Assets/PlayServ/Sub"
            var parts = folderPath.Split('/');
            if (parts.Length == 0) return;

            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static bool EnsureDeployApiServerAddress(PlayServConfig config)
        {
            if (config == null)
                return false;

            var serializedObject = new SerializedObject(config);
            var deployEndpointProperty = serializedObject.FindProperty(DeployApiServerAddressPropertyName);
            if (deployEndpointProperty == null)
                return false;

            var currentValue = deployEndpointProperty.stringValue?.Trim();
            var shouldReplace = string.IsNullOrWhiteSpace(currentValue);
            if (!shouldReplace)
                return false;

            deployEndpointProperty.stringValue = PlayServPackageDefaultsProvider.ResolveDeployApiServerAddress(null);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return true;
        }

        private static bool EnsureSchemaApiServerAddress(PlayServConfig config)
        {
            if (config == null)
                return false;

            var serializedObject = new SerializedObject(config);
            var schemaEndpointProperty = serializedObject.FindProperty(SchemaApiServerAddressPropertyName);
            if (schemaEndpointProperty == null)
                return false;

            var currentValue = schemaEndpointProperty.stringValue?.Trim();
            var shouldReplace = string.IsNullOrWhiteSpace(currentValue);
            if (!shouldReplace)
                return false;

            schemaEndpointProperty.stringValue = PlayServPackageDefaultsProvider.ResolveSchemaApiServerAddress(null);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return true;
        }

        private static bool EnsureDashboardAddress(PlayServConfig config)
        {
            if (config == null)
                return false;

            var serializedObject = new SerializedObject(config);
            var dashboardProperty = serializedObject.FindProperty(DashboardAddressPropertyName);
            if (dashboardProperty == null)
                return false;

            var currentValue = dashboardProperty.stringValue?.Trim();
            var shouldReplace = string.IsNullOrWhiteSpace(currentValue);
            if (!shouldReplace)
                return false;

            dashboardProperty.stringValue = PlayServPackageDefaultsProvider.ResolveDashboardAddress(null);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return true;
        }

        private static bool EnsureDefaultSdkVersion(PlayServConfig config)
        {
            if (config == null)
                return false;

            var serializedObject = new SerializedObject(config);
            var sdkVersionProperty = serializedObject.FindProperty("sdkVersion");
            if (sdkVersionProperty == null)
                return false;

            var currentValue = sdkVersionProperty.stringValue?.Trim();
            if (!PlayServPackageVersionProvider.TryResolveInstalledVersion(out var currentPackageVersion))
                return false;

            var shouldReplace = !string.Equals(currentValue, currentPackageVersion, System.StringComparison.Ordinal) ||
                                string.Equals(currentValue, LegacyDefaultSdkVersion, System.StringComparison.Ordinal);
            if (!shouldReplace)
                return false;

            sdkVersionProperty.stringValue = currentPackageVersion;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return true;
        }

        private static bool ApplyEnvironmentProfile(PlayServConfig config)
        {
            if (config == null)
                return false;

            if (PlayServSettingsResolver.TryResolveClientProjectSettings(config, out var resolved))
                return config.ApplySettings(resolved);

            if (PlayServPackageDefaultsProvider.TryLoadSettings(out var bakedSettings))
            {
                // Distributed package mode:
                // - keep user-entered values once set
                // - seed auth/game fields from baked package defaults only when currently empty
                // - always enforce baked endpoints
                var merged = config.ToSettings();
                if (string.IsNullOrWhiteSpace(merged.ClientToken))
                    merged.ClientToken = bakedSettings.ClientToken;
                if (string.IsNullOrWhiteSpace(merged.Authorization))
                    merged.Authorization = bakedSettings.Authorization;
                if (string.IsNullOrWhiteSpace(merged.GameId))
                    merged.GameId = bakedSettings.GameId;
                merged.BackendServerAddress = bakedSettings.BackendServerAddress;
                merged.DeployApiServerAddress = bakedSettings.DeployApiServerAddress;
                merged.SchemaApiServerAddress = bakedSettings.SchemaApiServerAddress;
                merged.DashboardAddress = bakedSettings.DashboardAddress;
                return config.ApplySettings(merged);
            }

            return false;
        }
    }
}
