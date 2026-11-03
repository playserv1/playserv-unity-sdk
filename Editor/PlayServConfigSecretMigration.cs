using Playserv.Wrapper;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal sealed class PlayServConfigSecretMigration : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        private static bool _migrating;
        static PlayServConfigSecretMigration()
        {
            EditorApplication.delayCall += () => MigrateAllConfigs();
        }

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            MigrateAllConfigs();
            ValidatePublicClientTokens();
        }

        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            foreach (var config in UnityEngine.Resources.FindObjectsOfTypeAll<PlayServConfig>())
                PlayServDeployCredentialStore.MigrateLegacySecrets(config);
        }

        internal static bool MigrateAllConfigs()
        {
            if (_migrating) return false;
            var changed = false;
            _migrating = true;
            try
            {
                var guids = AssetDatabase.FindAssets("t:PlayServConfig");
                foreach (var guid in guids)
                foreach (var config in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<PlayServConfig>())
                {
                    changed |= PlayServDeployCredentialStore.MigrateLegacySecrets(config);
                    if (config.HasLegacySecrets)
                        throw new BuildFailedException("PlayServ: legacy credentials could not be removed from a configuration asset.");
                }
                if (changed) AssetDatabase.SaveAssets();
            }
            finally { _migrating = false; }
            return changed;
        }

        internal static void ValidatePublicClientTokens()
        {
            ValidateAssets<PlayServConfig>(config => config.ClientToken, "PlayServConfig");
            ValidateAssets<PlayServPackageDefaults>(defaults => defaults.ClientToken, "PlayServPackageDefaults");
        }

        private static void ValidateAssets<T>(
            System.Func<T, string> resolveToken,
            string assetType)
            where T : UnityEngine.Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                var token = asset == null ? null : resolveToken(asset);
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                try
                {
                    global::Playserv.Runtime.Abstractions.PlayServCredentialPolicy.NormalizeClientToken(token);
                }
                catch (System.Exception exception)
                {
                    throw new BuildFailedException(
                        $"{assetType} at '{path}' contains an invalid client credential. {exception.Message}");
                }
            }
        }
    }

    internal sealed class PlayServConfigSecretImportProcessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(path => path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)))
                PlayServConfigSecretMigration.MigrateAllConfigs();
        }
    }
}
