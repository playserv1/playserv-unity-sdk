#if UNITY_EDITOR
using UnityEditor;

namespace Playserv.Editor
{
    internal sealed class PlayServEditorModuleFolderPostprocessor : AssetPostprocessor
    {
        private const string PackageSegment = "playserv-unity-sdk/";

        [InitializeOnLoadMethod]
        private static void SyncOnEditorLoad()
        {
            EditorApplication.delayCall += PlayServEditorModuleAvailability.SyncUnavailableModuleDefines;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (TouchesPlayServModuleFolders(importedAssets) ||
                TouchesPlayServModuleFolders(deletedAssets) ||
                TouchesPlayServModuleFolders(movedAssets) ||
                TouchesPlayServModuleFolders(movedFromAssetPaths))
            {
                PlayServEditorModuleAvailability.SyncUnavailableModuleDefines();
            }
        }

        private static bool TouchesPlayServModuleFolders(string[] assetPaths)
        {
            if (assetPaths == null)
                return false;

            for (var i = 0; i < assetPaths.Length; i++)
            {
                var path = (assetPaths[i] ?? string.Empty).Replace('\\', '/');
                if (path.IndexOf(PackageSegment + "Runtime/", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf(PackageSegment + "Editor/", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
