using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServLegacyPackageGenerationCleanup
    {
        private const string ThisScriptSuffix =
            "/Editor/Modules/PlayServLegacyPackageGenerationCleanup.cs";
        private const string RuntimeAsmdefRelativePath =
            "Runtime/Playserv.Runtime.asmdef";

        private static readonly string[] LegacyGeneratedFileRelativePaths =
        {
            "Runtime/Generated/Compatibility/PlayServCompatibility.g.cs",
            "Runtime/Generated/Modules/PlayServModuleRegistry.g.cs",
            "Runtime/Modules/Contracts/Generated/PlayServGeneratedModuleManifest.g.cs",
            "Runtime/Generated/.DS_Store"
        };

        private static readonly string[] LegacyGeneratedDirectoryRelativePaths =
        {
            "Runtime/Generated/Compatibility",
            "Runtime/Generated/Modules",
            "Runtime/Generated",
            "Runtime/Modules/Contracts/Generated"
        };

        private static readonly HashSet<string> LegacyInjectedAssemblyReferences =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Playserv.Runtime.Modules.Events",
                "Playserv.Runtime.Modules.DataSubscription",
                "Playserv.Runtime.Modules.RPC.Core",
                "Playserv.Runtime.Modules.RPC.Client",
                "Playserv.Runtime.Modules.Spawn",
                "Playserv.Runtime.Modules.Pulse",
                "Playserv.Runtime.Modules.Server",
                "Playserv.Runtime.Modules.AppleSignIn",
                "Playserv.Runtime.Modules.GoogleSignIn",
                "Playserv.Runtime.Transport.WebSocket",
                "Playserv.Runtime.Transport.Udp",
                "Playserv.Runtime.Transport.Rudp",
                "Playserv.Runtime.Transport.WebRtc"
            };

        static PlayServLegacyPackageGenerationCleanup()
        {
            EditorApplication.delayCall += CleanupOnEditorLoad;
        }

        private static void CleanupOnEditorLoad()
        {
            if (!PlayServPackagePathResolver.TryResolveRootForScript(
                    nameof(PlayServLegacyPackageGenerationCleanup),
                    ThisScriptSuffix,
                    out var packageRoot))
            {
                return;
            }

            if (CleanupPackageRoot(packageRoot.AbsolutePath) &&
                !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating)
            {
                AssetDatabase.Refresh();
            }
        }

        internal static bool CleanupPackageRoot(string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
                return false;

            var changed = false;
            for (var i = 0; i < LegacyGeneratedFileRelativePaths.Length; i++)
            {
                changed |= DeleteFileAndMeta(
                    Path.Combine(packageRoot, LegacyGeneratedFileRelativePaths[i]));
            }

            changed |= RestoreRuntimeAssemblyReferences(packageRoot);

            for (var i = 0; i < LegacyGeneratedDirectoryRelativePaths.Length; i++)
            {
                changed |= DeleteDirectoryAndMetaIfEmpty(
                    Path.Combine(packageRoot, LegacyGeneratedDirectoryRelativePaths[i]));
            }

            return changed;
        }

        private static bool RestoreRuntimeAssemblyReferences(string packageRoot)
        {
            var asmdefPath = Path.Combine(packageRoot, RuntimeAsmdefRelativePath);
            if (!File.Exists(asmdefPath))
                return false;

            AssemblyDefinitionModel model;
            try
            {
                model = JsonUtility.FromJson<AssemblyDefinitionModel>(File.ReadAllText(asmdefPath));
            }
            catch (Exception)
            {
                return false;
            }

            if (model == null ||
                !string.Equals(model.name, "Playserv.Runtime", StringComparison.Ordinal) ||
                model.references == null)
            {
                return false;
            }

            var restoredReferences = model.references
                .Where(reference => !LegacyInjectedAssemblyReferences.Contains(reference))
                .ToArray();
            if (restoredReferences.Length == model.references.Length)
                return false;

            model.references = restoredReferences;
            File.WriteAllText(
                asmdefPath,
                JsonUtility.ToJson(model, prettyPrint: true) + Environment.NewLine);
            return true;
        }

        private static bool DeleteFileAndMeta(string path)
        {
            var changed = DeleteFile(path);
            changed |= DeleteFile(path + ".meta");
            return changed;
        }

        private static bool DeleteFile(string path)
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }

        private static bool DeleteDirectoryAndMetaIfEmpty(string path)
        {
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
                return false;

            Directory.Delete(path);
            DeleteFile(path + ".meta");
            return true;
        }

        [Serializable]
        private sealed class AssemblyDefinitionModel
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
            public string[] excludePlatforms;
            public bool allowUnsafeCode;
            public bool overrideReferences;
            public string[] precompiledReferences;
            public bool autoReferenced;
            public string[] defineConstraints;
            public string[] versionDefines;
            public bool noEngineReferences;
        }
    }
}
