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
        private const string RuntimeAsmdefRelativePath = "Runtime/Playserv.Runtime.asmdef";
        private const string EditorAsmdefRelativePath = "Editor/Playserv.Editor.Core.asmdef";

        private static readonly string[] BaseReferences =
        {
            "Playserv.Runtime.Abstractions",
            "Playserv.Runtime.Serialization",
            "Playserv.Runtime.Core",
            "Playserv.Runtime.Http",
            "Playserv.Runtime.Modules"
        };

        private static readonly string[] EditorCoreReferences =
        {
            "Playserv.Runtime",
            "Playserv.Runtime.Modules"
        };

        [InitializeOnLoadMethod]
        private static void SyncOnEditorLoad()
        {
            PlayServModuleGraphSynchronizer.QueueSync();
        }

        public static void Sync()
        {
            Sync(importAssets: true);
        }

        internal static bool Sync(bool importAssets)
        {
            PlayServEditorModuleAvailability.SyncUnavailableModuleDefines();
            var state = PlayServRuntimeModuleDefines.LoadUserPreferenceState();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(ref state);
            PlayServRuntimeModuleDefines.NormalizeDependencies(ref state);

            return Sync(state, importAssets);
        }

        internal static bool Sync(PlayServRuntimeModuleState state, bool importAssets)
        {
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(ref state);
            PlayServRuntimeModuleDefines.NormalizeDependencies(ref state);

            var changed = false;
            changed |= SyncRuntimeAsmdefReferences(state, importAssets);
            changed |= SyncEditorAsmdefReferences(importAssets);
            return changed;
        }

        internal static bool HasRuntimeModuleReference(string moduleId)
        {
            if (!TryGetModuleAssemblyName(moduleId, out var assemblyName))
                return false;

            var asmdefPath = Path.Combine(PackageRootPath, RuntimeAsmdefRelativePath);
            if (!File.Exists(asmdefPath))
                return false;

            var model = JsonUtility.FromJson<AssemblyDefinitionModel>(File.ReadAllText(asmdefPath));
            var references = model?.references ?? Array.Empty<string>();
            for (var i = 0; i < references.Length; i++)
            {
                if (string.Equals(references[i], assemblyName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool SyncRuntimeAsmdefReferences(PlayServRuntimeModuleState state, bool importAssets)
        {
            var asmdefPath = Path.Combine(PackageRootPath, RuntimeAsmdefRelativePath);
            var nextModel = CreateRuntimeAsmdefModel(BuildRuntimeReferences(state));
            var nextJson = JsonUtility.ToJson(nextModel, prettyPrint: true) + Environment.NewLine;

            if (File.Exists(asmdefPath) && string.Equals(File.ReadAllText(asmdefPath), nextJson, StringComparison.Ordinal))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(asmdefPath));
            File.WriteAllText(asmdefPath, nextJson);
            if (importAssets)
                AssetDatabase.ImportAsset(ToAssetPath(asmdefPath), ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static AssemblyDefinitionModel CreateRuntimeAsmdefModel(string[] references)
        {
            return new AssemblyDefinitionModel
            {
                name = "Playserv.Runtime",
                rootNamespace = string.Empty,
                references = references ?? Array.Empty<string>(),
                includePlatforms = Array.Empty<string>(),
                excludePlatforms = Array.Empty<string>(),
                allowUnsafeCode = false,
                overrideReferences = false,
                precompiledReferences = Array.Empty<string>(),
                autoReferenced = true,
                defineConstraints = Array.Empty<string>(),
                versionDefines = Array.Empty<string>(),
                noEngineReferences = false
            };
        }

        private static bool SyncEditorAsmdefReferences(bool importAssets)
        {
            var asmdefPath = Path.Combine(PackageRootPath, EditorAsmdefRelativePath);
            if (!File.Exists(asmdefPath))
                return false;

            var json = File.ReadAllText(asmdefPath);
            var model = JsonUtility.FromJson<AssemblyDefinitionModel>(json);
            if (model == null)
                return false;

            var nextReferences = EditorCoreReferences;
            if (SequenceEqual(model.references, nextReferences))
                return false;

            model.references = nextReferences;
            File.WriteAllText(asmdefPath, JsonUtility.ToJson(model, prettyPrint: true) + Environment.NewLine);
            if (importAssets)
                AssetDatabase.ImportAsset(ToAssetPath(asmdefPath), ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static string[] BuildRuntimeReferences(PlayServRuntimeModuleState state)
        {
            var references = new List<string>(BaseReferences);
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (IsRootReferencedModuleEnabled(state, module.Id))
                    AddModuleReference(references, module.Id);
            }

            return references.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static bool IsRootReferencedModuleEnabled(PlayServRuntimeModuleState state, string moduleId)
        {
            switch (moduleId)
            {
                case PlayServModuleManifest.EventsId:
                    return state.Events;
                case PlayServModuleManifest.DataSubscriptionId:
                    return state.Data;
                case PlayServModuleManifest.RpcCoreId:
                    return state.Rpc || state.Server;
                case PlayServModuleManifest.ClientRpcId:
                    return state.Rpc;
                case PlayServModuleManifest.ServerId:
                    return state.Server;
                case PlayServModuleManifest.SpawnId:
                    return state.Spawn && state.Events;
                case PlayServModuleManifest.PulseId:
                    return state.Pulse;
                default:
                    return false;
            }
        }

        private static void AddModuleReference(List<string> references, string moduleId)
        {
            if (TryGetModuleAssemblyName(moduleId, out var assemblyName))
                references.Add(assemblyName);
        }

        private static bool TryGetModuleAssemblyName(string moduleId, out string assemblyName)
        {
            if (PlayServModuleManifest.TryGet(moduleId, out var module) &&
                !string.IsNullOrWhiteSpace(module.RootAssemblyReference))
            {
                assemblyName = module.RootAssemblyReference;
                return true;
            }

            assemblyName = null;
            return false;
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
