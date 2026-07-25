using System;
using System.Collections.Generic;
using System.Text;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServRuntimeModuleLifecycle
    {
        public static bool ApplyProfile(PlayServEditorModuleSettings settings, PlayServSdkProfile profile)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            settings.ApplyRuntimeProfile(profile);
            SyncProjectModuleGraph();
            return true;
        }

        public static bool CanUninstallModule(
            PlayServEditorModuleSettings settings,
            PlayServEditorModuleAvailability.PlayServRuntimeModuleDefinition module,
            out string reason)
        {
            reason = string.Empty;
            if (settings == null)
            {
                reason = "Module settings are not loaded.";
                return false;
            }

            if (module == null)
            {
                reason = "Module is not installed.";
                return false;
            }

            if (!PlayServModuleManifest.TryGet(module.Id, out var manifest))
            {
                reason = $"Unknown module id: {module.Id}.";
                return false;
            }

            if (manifest.AssetPaths.Length == 0)
            {
                reason = "This module does not own removable package folders.";
                return false;
            }

            if (!PlayServEditorModuleAvailability.TryGetModuleRoot(manifest, out var moduleRoot))
            {
                reason = "Module package root was not found.";
                return false;
            }

            if (moduleRoot.AssetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                if (PlayServCompanionPackageCatalog.TryGetByModuleId(
                        manifest.Id,
                        out var companionPackage))
                {
                    return PlayServCompanionPackageManager.CanRemove(
                        companionPackage,
                        settings,
                        out reason);
                }

                reason = "Remove this module package through Unity Package Manager.";
                return false;
            }

            if (module.IsEnabled(settings) && !settings.CanChangeRuntimeModule(module.Name))
            {
                reason = settings.GetRuntimeModuleBlockReason(module.Name);
                if (string.IsNullOrEmpty(reason))
                    reason = "Disable dependent modules first.";

                return false;
            }

            return true;
        }

        public static bool UninstallModule(
            PlayServEditorModuleSettings settings,
            PlayServEditorModuleAvailability.PlayServRuntimeModuleDefinition module,
            out string error)
        {
            error = string.Empty;
            if (!CanUninstallModule(settings, module, out error))
                return false;

            if (!PlayServModuleManifest.TryGet(module.Id, out var manifest))
            {
                error = $"Unknown module id: {module.Id}.";
                return false;
            }

            if (PlayServCompanionPackageCatalog.TryGetByModuleId(
                    manifest.Id,
                    out var companionPackage) &&
                PlayServEditorModuleAvailability.TryGetModuleRoot(
                    manifest,
                    out var companionModuleRoot) &&
                companionModuleRoot.AssetPath.StartsWith(
                    "Packages/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return PlayServCompanionPackageManager.Remove(
                    companionPackage,
                    settings,
                    out error);
            }

            if (module.IsEnabled(settings) && !module.SetEnabled(settings, false))
            {
                error = $"Failed to disable module before uninstall: {module.Name}.";
                return false;
            }

            // Remove the module from the project selection before deleting its assets.
            SyncProjectModuleGraph();

            if (!PlayServEditorModuleAvailability.TryGetModuleRoot(manifest, out var moduleRoot))
            {
                error = "Module package root was not found.";
                return false;
            }

            var removedAny = false;
            var failedPaths = new List<string>();
            for (var i = 0; i < manifest.AssetPaths.Length; i++)
            {
                var assetPath = moduleRoot.ToAssetPath(moduleRoot.ToAbsolutePath(manifest.AssetPaths[i]));
                if (!AssetDatabase.IsValidFolder(assetPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null)
                    continue;

                if (AssetDatabase.DeleteAsset(assetPath))
                {
                    removedAny = true;
                    continue;
                }

                failedPaths.Add(assetPath);
            }

            SyncProjectModuleGraph();
            AssetDatabase.Refresh();

            if (failedPaths.Count > 0)
            {
                error = BuildDeleteFailureMessage(failedPaths);
                return false;
            }

            return removedAny;
        }

        private static void SyncProjectModuleGraph()
        {
            PlayServModuleGraphSynchronizer.SyncNow();
        }

        private static string BuildDeleteFailureMessage(IReadOnlyList<string> failedPaths)
        {
            var builder = new StringBuilder("Failed to delete module assets:");
            for (var i = 0; i < failedPaths.Count; i++)
            {
                builder.AppendLine();
                builder.Append("- ");
                builder.Append(failedPaths[i]);
            }

            return builder.ToString();
        }

    }
}
