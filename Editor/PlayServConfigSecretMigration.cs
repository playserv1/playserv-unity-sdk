using Playserv.Wrapper;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal sealed class PlayServConfigSecretMigration : IPreprocessBuildWithReport
    {
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

        internal static bool MigrateAllConfigs()
        {
            var changed = false;
            var guids = AssetDatabase.FindAssets("t:PlayServConfig");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(path);
                changed |= PlayServDeployCredentialStore.MigrateLegacySecrets(config);
            }

            if (changed)
                AssetDatabase.SaveAssets();

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
}
