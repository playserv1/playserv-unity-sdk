using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServCoreAssemblyReferenceSync
    {
        private const string PackageFolderName = "playserv-unity-sdk";
        private const string ThisScriptSuffix = "/Editor/Window/PlayServCoreAssemblyReferenceSync.cs";
        private const string CoreAsmdefRelativePath = "Runtime/Playserv.Runtime.asmdef";

        private static readonly string[] BaseReferences =
        {
            "Playserv.Runtime.Abstractions",
            "Playserv.Runtime.Serialization",
            "Playserv.Runtime.Core",
            "Playserv.Runtime.Http",
            "Playserv.Runtime.Modules"
        };

        private static readonly ModuleAssemblyReference[] ModuleReferences =
        {
            new ModuleAssemblyReference(PlayServModuleManifest.EventsId, "Playserv.Runtime.Modules.Events"),
            new ModuleAssemblyReference(PlayServModuleManifest.DataSubscriptionId, "Playserv.Runtime.Modules.DataSubscription"),
            new ModuleAssemblyReference(PlayServModuleManifest.RpcCoreId, "Playserv.Runtime.Modules.RPC.Core"),
            new ModuleAssemblyReference(PlayServModuleManifest.ClientRpcId, "Playserv.Runtime.Modules.RPC.Client"),
            new ModuleAssemblyReference(PlayServModuleManifest.ServerRpcId, "Playserv.Runtime.Modules.ServerRPC"),
            new ModuleAssemblyReference(PlayServModuleManifest.LocalExecutionCoreId, "Playserv.Runtime.Modules.LocalExecution.Core"),
            new ModuleAssemblyReference(PlayServModuleManifest.ClientExecutionId, "Playserv.Runtime.Modules.LocalExecution.Client"),
            new ModuleAssemblyReference(PlayServModuleManifest.ServerLocalExecutionId, "Playserv.Runtime.Modules.LocalExecution.Server"),
            new ModuleAssemblyReference(PlayServModuleManifest.SpawnId, "Playserv.Runtime.Modules.Spawn"),
            new ModuleAssemblyReference(PlayServModuleManifest.PulseId, "Playserv.Runtime.Modules.Pulse")
        };

        [InitializeOnLoadMethod]
        private static void SyncOnEditorLoad()
        {
            EditorApplication.delayCall += Sync;
        }

        public static void Sync()
        {
            PlayServEditorModuleAvailability.SyncUnavailableModuleDefines();

            var asmdefPath = Path.Combine(PackageRootPath, CoreAsmdefRelativePath);
            if (!File.Exists(asmdefPath))
                return;

            var json = File.ReadAllText(asmdefPath);
            var model = JsonUtility.FromJson<AssemblyDefinitionModel>(json);
            if (model == null)
                return;

            var nextReferences = BuildReferences(ReadDefines());
            if (SequenceEqual(model.references, nextReferences))
                return;

            model.references = nextReferences;
            File.WriteAllText(asmdefPath, JsonUtility.ToJson(model, prettyPrint: true) + Environment.NewLine);
            AssetDatabase.ImportAsset(ToAssetPath(asmdefPath), ImportAssetOptions.ForceUpdate);
        }

        private static string[] BuildReferences(ISet<string> defines)
        {
            var references = new List<string>(BaseReferences);

            for (var i = 0; i < ModuleReferences.Length; i++)
            {
                var moduleReference = ModuleReferences[i];
                if (!IsModuleEnabled(defines, moduleReference.ModuleId))
                    continue;

                if (!IsModuleAvailable(moduleReference.ModuleId))
                    continue;

                references.Add(moduleReference.AssemblyName);
            }

            return references.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static bool IsModuleEnabled(ISet<string> defines, string moduleId)
        {
            return PlayServModuleManifest.TryGet(moduleId, out var module) &&
                   !defines.Contains(module.DisableDefine);
        }

        private static bool IsModuleAvailable(string moduleId)
        {
            if (!PlayServModuleManifest.TryGet(moduleId, out var module))
                return false;

            var paths = new List<string>();
            AddModuleAssetPaths(module, paths, new HashSet<string>(StringComparer.Ordinal));
            return paths.All(HasAssetPath);
        }

        private static void AddModuleAssetPaths(
            PlayServModuleManifestEntry module,
            List<string> paths,
            ISet<string> visited)
        {
            if (module == null || !visited.Add(module.Id))
                return;

            paths.AddRange(module.AssetPaths);
            paths.AddRange(module.HiddenDependencyAssetPaths);

            for (var i = 0; i < module.HiddenDependencyModuleIds.Length; i++)
            {
                if (PlayServModuleManifest.TryGet(module.HiddenDependencyModuleIds[i], out var dependency))
                    AddModuleAssetPaths(dependency, paths, visited);
            }
        }

        private static bool HasAssetPath(string relativePath)
        {
            var absolutePath = Path.Combine(PackageRootPath, relativePath);
            return Directory.Exists(absolutePath) || File.Exists(absolutePath);
        }

        private static ISet<string> ReadDefines()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var rawDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var symbols = rawDefines
                .Split(';')
                .Select(symbol => symbol.Trim())
                .Where(symbol => !string.IsNullOrEmpty(symbol));

            return new HashSet<string>(symbols, StringComparer.Ordinal);
        }

        private static bool SequenceEqual(string[] left, string[] right)
        {
            left = left ?? Array.Empty<string>();
            right = right ?? Array.Empty<string>();

            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static string _packageRootPath;

        private static string PackageRootPath
        {
            get
            {
                if (_packageRootPath == null)
                    _packageRootPath = ResolvePackageRootPath();

                return _packageRootPath;
            }
        }

        private static string ResolvePackageRootPath()
        {
            var guids = AssetDatabase.FindAssets($"{nameof(PlayServCoreAssemblyReferenceSync)} t:MonoScript");
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

        private static string ToAssetPath(string absolutePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(absolutePath);
            var relative = fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : absolutePath;
            return relative.Replace('\\', '/');
        }

        private readonly struct ModuleAssemblyReference
        {
            public ModuleAssemblyReference(string moduleId, string assemblyName)
            {
                ModuleId = moduleId;
                AssemblyName = assemblyName;
            }

            public string ModuleId { get; }

            public string AssemblyName { get; }
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
