using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServSdkVersionSync
    {
        private const string ThisScriptSuffix = "Editor/PlayServSdkVersionSync.cs";
        private const string SdkInfoRelativePath = "Runtime/Proxy/Common/SdkInfo.cs";
        private const string RuntimeSettingsRelativePath = "Runtime/Abstractions/PlayServRuntimeSettings.cs";

        private static readonly Regex SdkInfoVersionRegex = new Regex(
            "public\\s+const\\s+string\\s+Version\\s*=\\s*\"[^\"]*\";",
            RegexOptions.Compiled);

        private static readonly Regex RuntimeSettingsVersionRegex = new Regex(
            "public\\s+string\\s+SdkVersion\\s*\\{\\s*get;\\s*set;\\s*\\}\\s*=\\s*\"[^\"]*\";",
            RegexOptions.Compiled);

        static PlayServSdkVersionSync()
        {
            EditorApplication.delayCall += SyncFromPackageJsonIfNeeded;
        }

        [MenuItem("Tools/PlayServ/Internal/Sync SDK Version From package.json")]
        public static void SyncFromPackageJsonMenu()
        {
            if (SyncFromPackageJson(out var message))
                Debug.Log(message);
            else
                Debug.LogWarning(message);
        }

        public static bool SyncFromPackageJson(out string message)
        {
            message = string.Empty;

            if (!TryGetWritablePackageRoot(out var root, out message))
                return false;

            if (!PlayServPackageVersionProvider.TryResolvePackageJsonVersion(out var version))
            {
                message = "[PlayServ] Could not read SDK version from package.json.";
                return false;
            }

            var changed = false;
            changed |= UpdateSdkInfo(root.ToAbsolutePath(SdkInfoRelativePath), version);
            changed |= UpdateRuntimeSettings(root.ToAbsolutePath(RuntimeSettingsRelativePath), version);

            if (changed)
            {
                AssetDatabase.Refresh();
                message = $"[PlayServ] Synced SDK version from package.json: {version}.";
            }
            else
            {
                message = $"[PlayServ] SDK version already matches package.json: {version}.";
            }

            return true;
        }

        private static void SyncFromPackageJsonIfNeeded()
        {
            SyncFromPackageJson(out _);
        }

        private static bool TryGetWritablePackageRoot(out PlayServPackageRoot root, out string message)
        {
            root = null;
            message = string.Empty;

            if (!PlayServPackagePathResolver.TryResolveRootForScript(
                    nameof(PlayServSdkVersionSync),
                    ThisScriptSuffix,
                    out root))
            {
                message = "[PlayServ] Could not resolve SDK package root.";
                return false;
            }

            if (root.AssetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                message = "[PlayServ] SDK version sync is skipped for Package Manager cache packages.";
                return false;
            }

            return true;
        }

        private static bool UpdateSdkInfo(string path, string version)
        {
            return ReplaceInFile(
                path,
                SdkInfoVersionRegex,
                $"public const string Version = \"{version}\";");
        }

        private static bool UpdateRuntimeSettings(string path, string version)
        {
            return ReplaceInFile(
                path,
                RuntimeSettingsVersionRegex,
                $"public string SdkVersion {{ get; set; }} = \"{version}\";");
        }

        private static bool ReplaceInFile(string path, Regex pattern, string replacement)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var current = File.ReadAllText(path);
            var next = pattern.Replace(current, replacement, 1);
            if (string.Equals(current, next, StringComparison.Ordinal))
                return false;

            File.WriteAllText(path, next);
            return true;
        }
    }

    internal sealed class PlayServSdkVersionAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!ContainsPackageJson(importedAssets) && !ContainsPackageJson(movedAssets))
                return;

            PlayServSdkVersionSync.SyncFromPackageJson(out _);
        }

        private static bool ContainsPackageJson(string[] paths)
        {
            if (paths == null)
                return false;

            foreach (var path in paths)
            {
                if (path != null && path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
