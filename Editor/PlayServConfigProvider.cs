#if UNITY_EDITOR
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
        private const string LegacyDefaultSdkVersion = "1.0.0";
        private const string CurrentDefaultSdkVersion = "0.1.0";

        public static PlayServConfig GetOrCreate()
        {
            var config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(AssetPath);
            if (config != null)
            {
                var changed = EnsureBackendServerAddress(config);
                changed |= EnsureDeployApiServerAddress(config);
                changed |= EnsureSchemaApiServerAddress(config);
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

            backendProperty.stringValue = PlayServEnvDefaultsProvider.ResolveBackendServerAddress(null);
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

            deployEndpointProperty.stringValue = PlayServEnvDefaultsProvider.ResolveDeployApiServerAddress(null);
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

            schemaEndpointProperty.stringValue = PlayServEnvDefaultsProvider.ResolveSchemaApiServerAddress(null);
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
            var shouldReplace = string.IsNullOrWhiteSpace(currentValue) ||
                                string.Equals(currentValue, LegacyDefaultSdkVersion, System.StringComparison.Ordinal);
            if (!shouldReplace)
                return false;

            sdkVersionProperty.stringValue = CurrentDefaultSdkVersion;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return true;
        }

        private static bool ApplyEnvironmentProfile(PlayServConfig config)
        {
            if (config == null)
                return false;

            var resolved = PlayServEnvironmentResolver.ResolveSettingsForEditor(
                config,
                out _,
                out var profileApplied,
                out _);

            if (profileApplied)
                return config.ApplySettings(resolved);

            if (PlayServEnvDefaultsProvider.TryLoadSettings(out var bakedSettings))
                return config.ApplySettings(bakedSettings);

            return false;
        }
    }
}
#endif
