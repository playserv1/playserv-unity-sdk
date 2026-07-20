using System;
using System.Collections.Generic;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    internal sealed class PlayServEditorModuleFolderPostprocessor : AssetPostprocessor
    {
        [InitializeOnLoadMethod]
        private static void SyncOnEditorLoad()
        {
            EditorApplication.delayCall += SyncModuleState;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var restoredModules = GetRestoredRuntimeModuleIds(importedAssets, movedAssets);
            if (restoredModules.Count > 0)
                PlayServRuntimeModuleDefines.RestoreDefaultEnabledModules(restoredModules);

            if (TouchesPlayServModuleFolders(importedAssets) ||
                TouchesPlayServModuleFolders(deletedAssets) ||
                TouchesPlayServModuleFolders(movedAssets) ||
                TouchesPlayServModuleFolders(movedFromAssetPaths))
            {
                SyncModuleState();
            }
        }

        private static List<string> GetRestoredRuntimeModuleIds(string[] importedAssets, string[] movedAssets)
        {
            var moduleIds = new List<string>();
            AddRestoredRuntimeModuleIds(importedAssets, moduleIds);
            AddRestoredRuntimeModuleIds(movedAssets, moduleIds);
            return moduleIds;
        }

        private static void AddRestoredRuntimeModuleIds(string[] assetPaths, List<string> moduleIds)
        {
            if (assetPaths == null || moduleIds == null)
                return;

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (!module.DefaultEnabled || module.AssetPaths.Length == 0)
                    continue;

                for (var i = 0; i < assetPaths.Length; i++)
                {
                    if (!TouchesModuleAssetPath(assetPaths[i], module))
                        continue;

                    if (!moduleIds.Contains(module.Id))
                        moduleIds.Add(module.Id);

                    break;
                }
            }
        }

        private static bool TouchesModuleAssetPath(string assetPath, PlayServModuleManifestEntry module)
        {
            var path = (assetPath ?? string.Empty).Replace('\\', '/');
            if (!PlayServPackagePathResolver.TryGetPackageRelativeAssetPath(path, out var packageRelativePath))
                return false;

            for (var i = 0; i < module.AssetPaths.Length; i++)
            {
                var modulePath = (module.AssetPaths[i] ?? string.Empty).Replace('\\', '/').Trim('/').TrimEnd('/');
                if (packageRelativePath.Equals(modulePath, StringComparison.OrdinalIgnoreCase) ||
                    packageRelativePath.StartsWith(modulePath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SyncModuleState()
        {
            PlayServModuleGraphSynchronizer.SyncNow();
        }

        private static bool TouchesPlayServModuleFolders(string[] assetPaths)
        {
            if (assetPaths == null)
                return false;

            for (var i = 0; i < assetPaths.Length; i++)
            {
                var path = (assetPaths[i] ?? string.Empty).Replace('\\', '/');
                if (!PlayServPackagePathResolver.TryGetPackageRelativeAssetPath(path, out var packageRelativePath))
                    continue;

                if (packageRelativePath.StartsWith("Runtime/", System.StringComparison.OrdinalIgnoreCase) ||
                    packageRelativePath.StartsWith("Editor/", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
