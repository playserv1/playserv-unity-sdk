using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServRuntimeModuleLifecycle
    {
        private const string PackageFolderName = "playserv-unity-sdk";
        private const string ThisScriptSuffix = "/Editor/Window/PlayServRuntimeModuleLifecycle.cs";
        private static string _packageRootPath;

        public static bool ApplyProfile(PlayServEditorModuleSettings settings, PlayServSdkProfile profile)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            settings.ApplyRuntimeProfile(profile);
            SyncGeneratedAndReferences();
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

            if (module.IsEnabled(settings) && !module.SetEnabled(settings, false))
            {
                error = $"Failed to disable module before uninstall: {module.Name}.";
                return false;
            }

            // Make generated compatibility and root asmdef refs safe before removing files.
            SyncGeneratedAndReferences();

            var removedAny = false;
            var failedPaths = new List<string>();
            for (var i = 0; i < manifest.AssetPaths.Length; i++)
            {
                var assetPath = ToPackageAssetPath(manifest.AssetPaths[i]);
                if (!AssetDatabase.IsValidFolder(assetPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null)
                    continue;

                if (AssetDatabase.DeleteAsset(assetPath))
                {
                    removedAny = true;
                    continue;
                }

                failedPaths.Add(assetPath);
            }

            PlayServEditorModuleAvailability.SyncUnavailableModuleDefines();
            SyncGeneratedAndReferences();
            AssetDatabase.Refresh();

            if (failedPaths.Count > 0)
            {
                error = BuildDeleteFailureMessage(failedPaths);
                return false;
            }

            return removedAny;
        }

        private static void SyncGeneratedAndReferences()
        {
            PlayServGeneratedCompatibilityLayer.SyncNow();
            PlayServCoreAssemblyReferenceSync.Sync();
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

        private static string ToPackageAssetPath(string relativePath)
        {
            var absolutePath = Path.Combine(PackageRootPath, relativePath ?? string.Empty);
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var fullProjectRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(absolutePath);
            var assetPath = fullPath.StartsWith(fullProjectRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(fullProjectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : absolutePath;

            return assetPath.Replace('\\', '/');
        }

        private static string PackageRootPath =>
            _packageRootPath ?? (_packageRootPath = ResolvePackageRootPath());

        private static string ResolvePackageRootPath()
        {
            var guids = AssetDatabase.FindAssets($"{nameof(PlayServRuntimeModuleLifecycle)} t:MonoScript");
            for (var i = 0; i < guids.Length; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (!assetPath.EndsWith(ThisScriptSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rootAssetPath = assetPath.Substring(0, assetPath.Length - ThisScriptSuffix.Length);
                return ToAbsoluteAssetPath(rootAssetPath);
            }

            return Path.Combine(Application.dataPath, PackageFolderName);
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
