using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    public static class PlayServEditorModuleAvailability
    {
        private const string PackageFolderName = "playserv-unity-sdk";
        private const string ThisScriptSuffix = "/Editor/Window/PlayServEditorModuleAvailability.cs";
        private static string _packageRootPath;

        private static readonly PlayServRuntimeModuleDefinition[] RuntimeModuleDefinitions = BuildRuntimeModuleDefinitions();

        internal static IEnumerable<PlayServRuntimeModuleDefinition> AvailableRuntimeModules =>
            RuntimeModuleDefinitions.Where(module => IsRuntimeModuleAvailable(module.Name));

        public static IEnumerable<PlayServModuleManifestEntry> AvailableExportModules =>
            PlayServModuleManifest.ExportableRuntimeModules.Where(IsRuntimeModuleAvailable);

        public static bool RuntimeEvents => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleEvents);
        public static bool RuntimeData => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleData);
        public static bool RuntimeClientRpc => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleRpc);
        public static bool RuntimeServerRpc => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleServerRpc);
        public static bool RuntimeClientExecution => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleClientExecution);
        public static bool RuntimeLocalExecutionServer => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleLocalExecutionServer);
        public static bool RuntimeSpawn => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleSpawn);
        public static bool RuntimePulse => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModulePulse);

        public static bool EditorDeployment => HasFolder("Editor/Deploy");
        public static bool EditorModelSync => HasFolder("Editor/ModelGenerator");
        public static bool EditorCodegen => HasFolder("Editor/CodeGenerator") && HasFolder("Runtime/CodeGenerator/Shared");
        public static bool EditorEvents => HasFolder("Editor/Events") && RuntimeEvents;

        public static bool HasAnyEditorTool =>
            EditorDeployment ||
            EditorModelSync ||
            EditorCodegen;

        private static PlayServRuntimeModuleDefinition[] BuildRuntimeModuleDefinitions()
        {
            return PlayServModuleManifest.VisibleRuntimeModules
                .Select(CreateRuntimeModuleDefinition)
                .ToArray();
        }

        private static PlayServRuntimeModuleDefinition CreateRuntimeModuleDefinition(PlayServModuleManifestEntry module)
        {
            return new PlayServRuntimeModuleDefinition(
                module.Id,
                module.Label,
                module.Description,
                BuildRequiredFolderPaths(module),
                ToLabels(module.DependencyIds),
                BuildDependentLabels(module.Id),
                ResolveIsEnabled(module.Id),
                ResolveSetEnabled(module.Id));
        }

        private static string[] BuildRequiredFolderPaths(PlayServModuleManifestEntry module)
        {
            var paths = new List<string>();
            AddModuleAssetPaths(module, paths, new HashSet<string>(StringComparer.Ordinal));
            return paths
                .Distinct(StringComparer.Ordinal)
                .ToArray();
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

        private static string[] ToLabels(string[] moduleIds)
        {
            if (moduleIds == null || moduleIds.Length == 0)
                return Array.Empty<string>();

            return moduleIds
                .Select(PlayServModuleManifest.GetLabel)
                .ToArray();
        }

        private static string[] BuildDependentLabels(string moduleId)
        {
            return PlayServModuleManifest.VisibleRuntimeModules
                .Where(module => module.DependencyIds.Contains(moduleId, StringComparer.Ordinal))
                .Select(module => module.Label)
                .ToArray();
        }

        private static Func<PlayServEditorModuleSettings, bool> ResolveIsEnabled(string moduleId)
        {
            switch (moduleId)
            {
                case PlayServModuleManifest.EventsId:
                    return settings => settings.RuntimeEvents;
                case PlayServModuleManifest.DataSubscriptionId:
                    return settings => settings.RuntimeData;
                case PlayServModuleManifest.ClientRpcId:
                    return settings => settings.RuntimeRpc;
                case PlayServModuleManifest.ServerRpcId:
                    return settings => settings.RuntimeServerRpc;
                case PlayServModuleManifest.ClientExecutionId:
                    return settings => settings.RuntimeClientExecution;
                case PlayServModuleManifest.ServerLocalExecutionId:
                    return settings => settings.RuntimeLocalExecutionServer;
                case PlayServModuleManifest.SpawnId:
                    return settings => settings.RuntimeSpawn;
                case PlayServModuleManifest.PulseId:
                    return settings => settings.RuntimePulse;
                default:
                    return _ => false;
            }
        }

        private static Func<PlayServEditorModuleSettings, bool, bool> ResolveSetEnabled(string moduleId)
        {
            switch (moduleId)
            {
                case PlayServModuleManifest.EventsId:
                    return (settings, enabled) => settings.SetRuntimeEvents(enabled);
                case PlayServModuleManifest.DataSubscriptionId:
                    return (settings, enabled) => settings.SetRuntimeData(enabled);
                case PlayServModuleManifest.ClientRpcId:
                    return (settings, enabled) => settings.SetRuntimeRpc(enabled);
                case PlayServModuleManifest.ServerRpcId:
                    return (settings, enabled) => settings.SetRuntimeServerRpc(enabled);
                case PlayServModuleManifest.ClientExecutionId:
                    return (settings, enabled) => settings.SetRuntimeClientExecution(enabled);
                case PlayServModuleManifest.ServerLocalExecutionId:
                    return (settings, enabled) => settings.SetRuntimeLocalExecutionServer(enabled);
                case PlayServModuleManifest.SpawnId:
                    return (settings, enabled) => settings.SetRuntimeSpawn(enabled);
                case PlayServModuleManifest.PulseId:
                    return (settings, enabled) => settings.SetRuntimePulse(enabled);
                default:
                    return (_, __) => false;
            }
        }

        public static bool IsRuntimeModuleAvailable(string moduleName)
        {
            if (string.Equals(moduleName, PlayServEditorModuleSettings.RuntimeModuleRpcCore, StringComparison.Ordinal))
                return RuntimeClientRpc || RuntimeServerRpc;

            return IsRuntimeModuleAvailable(moduleName, new HashSet<string>(StringComparer.Ordinal));
        }

        public static bool IsRuntimeModuleAvailable(PlayServModuleManifestEntry module)
        {
            return module != null && IsRuntimeModuleAvailable(module.Label);
        }

        private static bool IsRuntimeModuleAvailable(string moduleName, ISet<string> visited)
        {
            if (string.Equals(moduleName, PlayServEditorModuleSettings.RuntimeModuleRpcCore, StringComparison.Ordinal))
                return IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleRpc, visited) ||
                       IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleServerRpc, visited);

            var module = FindRuntimeModule(moduleName);
            if (module == null || !module.HasRequiredFolders)
                return false;

            if (!visited.Add(module.Name))
                return true;

            for (var i = 0; i < module.Dependencies.Length; i++)
            {
                if (!IsRuntimeModuleAvailable(module.Dependencies[i], visited))
                    return false;
            }

            return true;
        }

        internal static void NormalizeAvailableRuntimeState(ref PlayServRuntimeModuleState state)
        {
            state.Events &= RuntimeEvents;
            state.Data &= RuntimeData;
            state.Rpc &= RuntimeClientRpc;
            state.ServerRpc &= RuntimeServerRpc;
            state.ClientExecution &= RuntimeClientExecution;
            if (!state.ClientExecution)
            {
                state.Events = false;
                state.Rpc = false;
                state.Spawn = false;
                state.Pulse = false;
            }

            state.LocalExecutionServer &= RuntimeLocalExecutionServer;
            if (!state.LocalExecutionServer)
                state.ServerRpc = false;
            state.Spawn &= RuntimeSpawn;
            state.Pulse &= RuntimePulse;
        }

        public static void SyncUnavailableModuleDefines()
        {
            var defines = ReadDefines();
            var changed = PlayServRuntimeModuleDefines.RemoveLegacyRuntimeModuleDefines(defines);

            changed |= SyncModuleDefine(defines, PlayServModuleManifest.EventsId, RuntimeEvents);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.DataSubscriptionId, RuntimeData);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.ClientRpcId, RuntimeClientRpc);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.ServerRpcId, RuntimeServerRpc);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.RpcCoreId, RuntimeClientRpc || RuntimeServerRpc);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.ClientExecutionId, RuntimeClientExecution);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.LocalExecutionCoreId, RuntimeClientExecution || RuntimeLocalExecutionServer);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.ServerLocalExecutionId, RuntimeLocalExecutionServer);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.SpawnId, RuntimeSpawn);
            changed |= SyncModuleDefine(defines, PlayServModuleManifest.PulseId, RuntimePulse);

            changed |= SetDisabled(defines, Const.DefineDisableEditorDeployment, !EditorDeployment);
            changed |= SetDisabled(defines, Const.DefineDisableEditorModelSync, !EditorModelSync);
            changed |= SetDisabled(defines, Const.DefineDisableEditorCodegen, !EditorCodegen);

            if (changed)
                WriteDefines(defines);
        }

        private static bool HasAssetPath(string relativePath)
        {
            var absolutePath = Path.Combine(PackageRootPath, relativePath);
            return Directory.Exists(absolutePath) || File.Exists(absolutePath);
        }

        private static bool HasFolder(string relativePath)
        {
            return Directory.Exists(Path.Combine(PackageRootPath, relativePath));
        }

        private static PlayServRuntimeModuleDefinition FindRuntimeModule(string moduleName)
        {
            for (var i = 0; i < RuntimeModuleDefinitions.Length; i++)
            {
                if (string.Equals(RuntimeModuleDefinitions[i].Name, moduleName, StringComparison.Ordinal))
                    return RuntimeModuleDefinitions[i];
            }

            return null;
        }

        private static string PackageRootPath =>
            _packageRootPath ?? (_packageRootPath = ResolvePackageRootPath());

        private static string ResolvePackageRootPath()
        {
            var guids = AssetDatabase.FindAssets($"{nameof(PlayServEditorModuleAvailability)} t:MonoScript");
            for (var i = 0; i < guids.Length; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (!assetPath.EndsWith(ThisScriptSuffix, System.StringComparison.OrdinalIgnoreCase))
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

        private static bool SetDisabled(ISet<string> defines, string symbol, bool disabled)
        {
            return disabled ? defines.Add(symbol) : defines.Remove(symbol);
        }

        private static bool SyncModuleDefine(ISet<string> defines, string moduleId, bool available)
        {
            var module = PlayServModuleManifest.GetRequired(moduleId);
            var shouldDisable = !available || PlayServRuntimeModuleDefines.IsUserDisabled(moduleId);
            return SetDisabled(defines, module.DisableDefine, shouldDisable);
        }

        private static ISet<string> ReadDefines()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var rawDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var symbols = rawDefines
                .Split(';')
                .Select(symbol => symbol.Trim())
                .Where(symbol => !string.IsNullOrEmpty(symbol));

            return new HashSet<string>(symbols, System.StringComparer.Ordinal);
        }

        private static void WriteDefines(ISet<string> defines)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var value = string.Join(";", defines.OrderBy(symbol => symbol, System.StringComparer.Ordinal));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, value);
        }

        internal sealed class PlayServRuntimeModuleDefinition
        {
            private readonly string[] _folderPaths;
            private readonly Func<PlayServEditorModuleSettings, bool> _isEnabled;
            private readonly Func<PlayServEditorModuleSettings, bool, bool> _setEnabled;

            public PlayServRuntimeModuleDefinition(
                string id,
                string name,
                string description,
                string[] folderPaths,
                string[] dependencies,
                string[] dependents,
                Func<PlayServEditorModuleSettings, bool> isEnabled,
                Func<PlayServEditorModuleSettings, bool, bool> setEnabled)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                Name = name ?? throw new ArgumentNullException(nameof(name));
                Description = description ?? string.Empty;
                Dependencies = dependencies ?? Array.Empty<string>();
                Dependents = dependents ?? Array.Empty<string>();
                _folderPaths = folderPaths ?? Array.Empty<string>();
                _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
                _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
            }

            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
            public string[] Dependencies { get; }
            public string[] Dependents { get; }

            public bool HasRequiredFolders
            {
                get
                {
                    for (var i = 0; i < _folderPaths.Length; i++)
                    {
                        if (!HasAssetPath(_folderPaths[i]))
                            return false;
                    }

                    return true;
                }
            }

            public bool IsEnabled(PlayServEditorModuleSettings settings)
            {
                return settings != null && _isEnabled(settings);
            }

            public bool SetEnabled(PlayServEditorModuleSettings settings, bool enabled)
            {
                return settings != null && _setEnabled(settings, enabled);
            }
        }
    }
}
