using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    public static class PlayServEditorModuleAvailability
    {
        private const string ThisScriptSuffix = "/Editor/Window/PlayServEditorModuleAvailability.cs";
        private static PlayServPackageRoot _packageRoot;

        private static readonly PlayServRuntimeModuleDefinition[] RuntimeModuleDefinitions = BuildRuntimeModuleDefinitions();

        internal static IEnumerable<PlayServRuntimeModuleDefinition> AvailableRuntimeModules =>
            RuntimeModuleDefinitions.Where(module => IsRuntimeModuleAvailable(module.Name));

        public static IEnumerable<PlayServModuleManifestEntry> AvailableExportModules =>
            PlayServModuleManifest.ExportableRuntimeModules.Where(IsRuntimeModuleAvailable);

        public static bool RuntimeEvents => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleEvents);
        public static bool RuntimeData => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleData);
        public static bool RuntimeClientRpc => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleRpc);
        public static bool RuntimeServer => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleServer);
        public static bool RuntimeClientExecution => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleClientExecution);
        public static bool RuntimeSpawn => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleSpawn);
        public static bool RuntimePulse => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModulePulse);
        public static bool RuntimeTransportWebSocket => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleTransportWebSocket);
        public static bool RuntimeTransportUdp => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleTransportUdp);
        public static bool RuntimeTransportRudp => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleTransportRudp);
        public static bool RuntimeTransportWebRtc => IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleTransportWebRtc);

        public static bool EditorDeployment => HasFolder("Editor/Deploy");
        public static bool EditorModelSync => HasFolder("Editor/ModelGenerator");
        public static bool EditorCodegen => HasFolder("Editor/CodeGenerator") && HasFolder("Runtime/CodeGenerator/Shared");
        public static bool EditorEvents => HasFolder("Editor/Events") && RuntimeEvents;
        public static bool EditorModuleStressTests => HasFolder("Editor/ModuleStressTests");

        public static bool HasAnyEditorTool =>
            EditorDeployment ||
            EditorModelSync ||
            EditorCodegen ||
            EditorModuleStressTests;

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
                case PlayServModuleManifest.ServerId:
                    return settings => settings.RuntimeServer;
                case PlayServModuleManifest.ClientExecutionId:
                    return settings => settings.RuntimeClientExecution;
                case PlayServModuleManifest.SpawnId:
                    return settings => settings.RuntimeSpawn;
                case PlayServModuleManifest.PulseId:
                    return settings => settings.RuntimePulse;
                case PlayServModuleManifest.TransportWebSocketId:
                    return settings => settings.RuntimeTransportWebSocket;
                case PlayServModuleManifest.TransportUdpId:
                    return settings => settings.RuntimeTransportUdp;
                case PlayServModuleManifest.TransportRudpId:
                    return settings => settings.RuntimeTransportRudp;
                case PlayServModuleManifest.TransportWebRtcId:
                    return settings => settings.RuntimeTransportWebRtc;
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
                case PlayServModuleManifest.ServerId:
                    return (settings, enabled) => settings.SetRuntimeServer(enabled);
                case PlayServModuleManifest.ClientExecutionId:
                    return (settings, enabled) => settings.SetRuntimeClientExecution(enabled);
                case PlayServModuleManifest.SpawnId:
                    return (settings, enabled) => settings.SetRuntimeSpawn(enabled);
                case PlayServModuleManifest.PulseId:
                    return (settings, enabled) => settings.SetRuntimePulse(enabled);
                case PlayServModuleManifest.TransportWebSocketId:
                    return (settings, enabled) => settings.SetRuntimeTransportWebSocket(enabled);
                case PlayServModuleManifest.TransportUdpId:
                    return (settings, enabled) => settings.SetRuntimeTransportUdp(enabled);
                case PlayServModuleManifest.TransportRudpId:
                    return (settings, enabled) => settings.SetRuntimeTransportRudp(enabled);
                case PlayServModuleManifest.TransportWebRtcId:
                    return (settings, enabled) => settings.SetRuntimeTransportWebRtc(enabled);
                default:
                    return (_, __) => false;
            }
        }

        public static bool IsRuntimeModuleAvailable(string moduleName)
        {
            if (string.Equals(moduleName, PlayServEditorModuleSettings.RuntimeModuleRpcCore, StringComparison.Ordinal))
                return RuntimeClientRpc || RuntimeServer;

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
                       IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleServer, visited);

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
            state.Server &= RuntimeServer;
            state.ClientExecution &= RuntimeClientExecution;
            if (!state.ClientExecution)
            {
                state.Events = false;
                state.Rpc = false;
                state.Spawn = false;
                state.Pulse = false;
                state.TransportWebSocket = false;
                state.TransportUdp = false;
                state.TransportRudp = false;
                state.TransportWebRtc = false;
            }

            state.Spawn &= RuntimeSpawn;
            state.Pulse &= RuntimePulse;
            state.TransportWebSocket &= RuntimeTransportWebSocket;
            state.TransportUdp &= RuntimeTransportUdp;
            state.TransportRudp &= RuntimeTransportRudp;
            state.TransportWebRtc &= RuntimeTransportWebRtc;
        }

        public static void SyncUnavailableModuleDefines()
        {
            PlayServRuntimeModuleDefines.RemoveStaleDefaultDisableDefines(GetAvailableDefaultEnabledModuleIds());

            var defines = ReadDefines();
            var changed = PlayServRuntimeModuleDefines.RemoveLegacyRuntimeModuleDefines(defines);

            changed |= SetDisabled(defines, Const.DefineDisableEditorDeployment, !EditorDeployment);
            changed |= SetDisabled(defines, Const.DefineDisableEditorModelSync, !EditorModelSync);
            changed |= SetDisabled(defines, Const.DefineDisableEditorCodegen, !EditorCodegen);

            if (changed)
                WriteDefines(defines);
        }

        private static IEnumerable<string> GetAvailableDefaultEnabledModuleIds()
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (!module.DefaultEnabled || !IsRuntimeModuleAvailable(module))
                    continue;

                yield return module.Id;
            }
        }

        private static bool HasAssetPath(string relativePath)
        {
            if (!TryGetPackageRoot(out var packageRoot))
                return false;

            var absolutePath = packageRoot.ToAbsolutePath(relativePath);
            return Directory.Exists(absolutePath) || File.Exists(absolutePath);
        }

        private static bool HasFolder(string relativePath)
        {
            return TryGetPackageRoot(out var packageRoot) &&
                   Directory.Exists(packageRoot.ToAbsolutePath(relativePath));
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

        private static bool TryGetPackageRoot(out PlayServPackageRoot packageRoot)
        {
            if (_packageRoot != null)
            {
                if (_packageRoot.Exists)
                {
                    packageRoot = _packageRoot;
                    return true;
                }

                _packageRoot = null;
            }

            if (!PlayServPackagePathResolver.TryResolveRootForScript(
                nameof(PlayServEditorModuleAvailability),
                ThisScriptSuffix,
                out packageRoot))
            {
                return false;
            }

            _packageRoot = packageRoot;
            return true;
        }

        private static bool SetDisabled(ISet<string> defines, string symbol, bool disabled)
        {
            return disabled ? defines.Add(symbol) : defines.Remove(symbol);
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
