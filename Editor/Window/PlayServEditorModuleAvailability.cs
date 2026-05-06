#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServEditorModuleAvailability
    {
        private const string PackageFolderName = "playserv-unity-sdk";
        private const string ThisScriptSuffix = "/Editor/Window/PlayServEditorModuleAvailability.cs";
        private static string _packageRootPath;

        private static readonly PlayServRuntimeModuleDefinition[] RuntimeModuleDefinitions =
        {
            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModuleClientExecution,
                "Client-side transport execution surface. Disable this for server-only SDK builds.",
                new[] { "Runtime/Modules/LocalExecution/Core", "Runtime/Modules/LocalExecution/Client" },
                dependencies: null,
                dependents: new[]
                {
                    PlayServEditorModuleSettings.RuntimeModuleEvents,
                    PlayServEditorModuleSettings.RuntimeModuleRpc,
                    PlayServEditorModuleSettings.RuntimeModulePulse
                },
                settings => settings.RuntimeClientExecution,
                (settings, enabled) => settings.SetRuntimeClientExecution(enabled)),

            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModuleEvents,
                "Typed publish/subscribe runtime and typed event API generation controls. Cannot be disabled while dependent modules are enabled.",
                new[] { "Runtime/Modules/Events" },
                dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleClientExecution },
                dependents: new[]
                {
                    PlayServEditorModuleSettings.RuntimeModuleData,
                    PlayServEditorModuleSettings.RuntimeModuleSpawn
                },
                settings => settings.RuntimeEvents,
                (settings, enabled) => settings.SetRuntimeEvents(enabled)),

            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModuleData,
                "Shared entity query, mutation, polling, and transport subscription APIs. Depends on Events.",
                new[] { "Runtime/Modules/DataSubscription" },
                dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleEvents },
                dependents: null,
                settings => settings.RuntimeData,
                (settings, enabled) => settings.SetRuntimeData(enabled)),

            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModuleRpc,
                "Client-side remote RPC commands, generated RPC helpers, and Invoke* wrapper APIs.",
                new[] { "Runtime/Modules/RPC/Core", "Runtime/Modules/RPC/Client" },
                dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleClientExecution },
                dependents: null,
                settings => settings.RuntimeRpc,
                (settings, enabled) => settings.SetRuntimeRpc(enabled)),

            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModuleServerRpc,
                "Server-side in-process RPC invoker, service registry, and PlayServServerRpc API.",
                new[] { "Runtime/Modules/RPC/Core", "Runtime/Modules/ServerRPC" },
                dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleLocalExecutionServer },
                dependents: null,
                settings => settings.RuntimeServerRpc,
                (settings, enabled) => settings.SetRuntimeServerRpc(enabled)),

            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModuleLocalExecutionServer,
                "Server-side local command/event execution bridge and default in-process handlers.",
                new[] { "Runtime/Modules/LocalExecution/Core", "Runtime/Modules/LocalExecution/Server" },
                dependencies: null,
                dependents: new[] { PlayServEditorModuleSettings.RuntimeModuleServerRpc },
                settings => settings.RuntimeLocalExecutionServer,
                (settings, enabled) => settings.SetRuntimeLocalExecutionServer(enabled)),

            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModuleSpawn,
                "NetworkObject/NetworkTransform helpers and Resources-based spawn facade. Depends on Events.",
                new[] { "Runtime/Modules/Spawn" },
                dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleEvents },
                dependents: null,
                settings => settings.RuntimeSpawn,
                (settings, enabled) => settings.SetRuntimeSpawn(enabled)),

            new PlayServRuntimeModuleDefinition(
                PlayServEditorModuleSettings.RuntimeModulePulse,
                "Realtime config placeholder module and future feature flag surface.",
                new[] { "Runtime/Modules/Pulse" },
                dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleClientExecution },
                dependents: null,
                settings => settings.RuntimePulse,
                (settings, enabled) => settings.SetRuntimePulse(enabled))
        };

        public static IEnumerable<PlayServRuntimeModuleDefinition> AvailableRuntimeModules =>
            RuntimeModuleDefinitions.Where(module => IsRuntimeModuleAvailable(module.Name));

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

        public static bool IsRuntimeModuleAvailable(string moduleName)
        {
            if (string.Equals(moduleName, PlayServEditorModuleSettings.RuntimeModuleRpcCore, StringComparison.Ordinal))
                return RuntimeClientRpc || RuntimeServerRpc;

            return IsRuntimeModuleAvailable(moduleName, new HashSet<string>(StringComparer.Ordinal));
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

        public static void NormalizeAvailableRuntimeState(ref PlayServRuntimeModuleState state)
        {
            state.Events &= RuntimeEvents;
            state.Data &= RuntimeData;
            state.Rpc &= RuntimeClientRpc;
            state.ServerRpc &= RuntimeServerRpc;
            state.ClientExecution &= RuntimeClientExecution;
            if (!state.ClientExecution)
            {
                state.Events = false;
                state.Data = false;
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
            var changed = false;

            changed |= DisableIfUnavailable(defines, Const.DefineDisableEvents, RuntimeEvents);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableData, RuntimeData);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableClientRpc, RuntimeClientRpc);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableServerRpc, RuntimeServerRpc);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableRpcCore, RuntimeClientRpc || RuntimeServerRpc);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableClientExecution, RuntimeClientExecution);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableLocalExecutionCore, RuntimeClientExecution || RuntimeLocalExecutionServer);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableLocalExecutionServer, RuntimeLocalExecutionServer);
            changed |= DisableIfUnavailable(defines, Const.DefineDisableSpawn, RuntimeSpawn);
            changed |= DisableIfUnavailable(defines, Const.DefineDisablePulse, RuntimePulse);

            changed |= SetDisabled(defines, Const.DefineDisableEditorDeployment, !EditorDeployment);
            changed |= SetDisabled(defines, Const.DefineDisableEditorModelSync, !EditorModelSync);
            changed |= SetDisabled(defines, Const.DefineDisableEditorCodegen, !EditorCodegen);

            if (changed)
                WriteDefines(defines);
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

        private static bool DisableIfUnavailable(ISet<string> defines, string symbol, bool available)
        {
            return available ? false : defines.Add(symbol);
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
                string name,
                string description,
                string[] folderPaths,
                string[] dependencies,
                string[] dependents,
                Func<PlayServEditorModuleSettings, bool> isEnabled,
                Func<PlayServEditorModuleSettings, bool, bool> setEnabled)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
                Description = description ?? string.Empty;
                Dependencies = dependencies ?? Array.Empty<string>();
                Dependents = dependents ?? Array.Empty<string>();
                _folderPaths = folderPaths ?? Array.Empty<string>();
                _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
                _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
            }

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
                        if (!HasFolder(_folderPaths[i]))
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
#endif
