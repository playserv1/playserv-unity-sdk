using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    internal static class PlayServRuntimeModuleDefines
    {
        private static readonly char[] DefineSeparators = { ';' };
        private static readonly string[] LegacyRuntimeModuleDisableDefines =
        {
            "PLAYSERV_DISABLE_EVENTS",
            "PLAYSERV_DISABLE_DATA",
            "PLAYSERV_DISABLE_RPC_CORE",
            "PLAYSERV_DISABLE_CLIENT_RPC",
            "PLAYSERV_DISABLE_SERVER_RPC",
            "PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE",
            "PLAYSERV_DISABLE_CLIENT_EXECUTION",
            "PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER",
            "PLAYSERV_DISABLE_SPAWN",
            "PLAYSERV_DISABLE_PULSE"
        };

        public static PlayServRuntimeModuleState Load()
        {
            var defines = ReadDefines();
            var rpcCoreDisabled = IsDisabled(defines, PlayServModuleManifest.RpcCoreId);
            var localExecutionCoreDisabled = IsDisabled(defines, PlayServModuleManifest.LocalExecutionCoreId);
            var state = new PlayServRuntimeModuleState
            {
                Events = IsEnabled(defines, PlayServModuleManifest.EventsId),
                Data = IsEnabled(defines, PlayServModuleManifest.DataSubscriptionId),
                Rpc = !rpcCoreDisabled && IsEnabled(defines, PlayServModuleManifest.ClientRpcId),
                ServerRpc = !rpcCoreDisabled && IsEnabled(defines, PlayServModuleManifest.ServerRpcId),
                ClientExecution = !localExecutionCoreDisabled && IsEnabled(defines, PlayServModuleManifest.ClientExecutionId),
                LocalExecutionServer = !localExecutionCoreDisabled && IsEnabled(defines, PlayServModuleManifest.ServerLocalExecutionId),
                Spawn = IsEnabled(defines, PlayServModuleManifest.SpawnId) &&
                        IsEnabled(defines, PlayServModuleManifest.EventsId),
                Pulse = IsEnabled(defines, PlayServModuleManifest.PulseId)
            };
            NormalizeDependencies(ref state);
            return state;
        }

        public static bool Apply(PlayServRuntimeModuleState state)
        {
            NormalizeDependencies(ref state);
            WriteUserDisabledPrefs(state);

            var defines = ReadDefines();
            var changed = RemoveLegacyRuntimeModuleDefines(defines);
            var rpcCoreEnabled = state.Rpc || state.ServerRpc;
            var localExecutionCoreEnabled = state.ClientExecution || state.LocalExecutionServer;

            changed |= SetModuleDisabled(defines, PlayServModuleManifest.EventsId, !state.Events);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.DataSubscriptionId, !state.Data);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.RpcCoreId, !rpcCoreEnabled);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.ClientRpcId, !state.Rpc);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.ServerRpcId, !state.ServerRpc);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.LocalExecutionCoreId, !localExecutionCoreEnabled);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.ClientExecutionId, !state.ClientExecution);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.ServerLocalExecutionId, !state.LocalExecutionServer);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.SpawnId, !state.Spawn);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.PulseId, !state.Pulse);

            if (!changed)
                return false;

            WriteDefines(defines);
            return true;
        }

        public static bool IsUserDisabled(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId))
                return false;

            return EditorPrefs.GetBool(BuildUserDisabledPrefKey(moduleId), false);
        }

        public static bool RemoveLegacyRuntimeModuleDefines(ISet<string> defines)
        {
            if (defines == null)
                return false;

            var changed = false;
            for (var i = 0; i < LegacyRuntimeModuleDisableDefines.Length; i++)
                changed |= defines.Remove(LegacyRuntimeModuleDisableDefines[i]);

            return changed;
        }

        public static void NormalizeDependencies(ref PlayServRuntimeModuleState state)
        {
            if (!state.ClientExecution)
            {
                state.Events = false;
                state.Rpc = false;
                state.Spawn = false;
                state.Pulse = false;
            }

            if (!state.LocalExecutionServer)
                state.ServerRpc = false;

            if (!state.Events)
                state.Spawn = false;
        }

        private static bool SetDisabled(ISet<string> defines, string symbol, bool disabled)
        {
            return disabled ? defines.Add(symbol) : defines.Remove(symbol);
        }

        private static bool SetModuleDisabled(ISet<string> defines, string moduleId, bool disabled)
        {
            var module = PlayServModuleManifest.GetRequired(moduleId);
            return SetDisabled(defines, module.DisableDefine, disabled);
        }

        private static void WriteUserDisabledPrefs(PlayServRuntimeModuleState state)
        {
            var rpcCoreEnabled = state.Rpc || state.ServerRpc;
            var localExecutionCoreEnabled = state.ClientExecution || state.LocalExecutionServer;

            SetUserDisabled(PlayServModuleManifest.EventsId, !state.Events);
            SetUserDisabled(PlayServModuleManifest.DataSubscriptionId, !state.Data);
            SetUserDisabled(PlayServModuleManifest.RpcCoreId, !rpcCoreEnabled);
            SetUserDisabled(PlayServModuleManifest.ClientRpcId, !state.Rpc);
            SetUserDisabled(PlayServModuleManifest.ServerRpcId, !state.ServerRpc);
            SetUserDisabled(PlayServModuleManifest.LocalExecutionCoreId, !localExecutionCoreEnabled);
            SetUserDisabled(PlayServModuleManifest.ClientExecutionId, !state.ClientExecution);
            SetUserDisabled(PlayServModuleManifest.ServerLocalExecutionId, !state.LocalExecutionServer);
            SetUserDisabled(PlayServModuleManifest.SpawnId, !state.Spawn);
            SetUserDisabled(PlayServModuleManifest.PulseId, !state.Pulse);
        }

        private static void SetUserDisabled(string moduleId, bool disabled)
        {
            EditorPrefs.SetBool(BuildUserDisabledPrefKey(moduleId), disabled);
        }

        private static string BuildUserDisabledPrefKey(string moduleId)
        {
            return Const.PrefRuntimeModuleUserDisabledPrefix + moduleId;
        }

        private static bool IsEnabled(ISet<string> defines, string moduleId)
        {
            return !IsDisabled(defines, moduleId);
        }

        private static bool IsDisabled(ISet<string> defines, string moduleId)
        {
            var module = PlayServModuleManifest.GetRequired(moduleId);
            return defines.Contains(module.DisableDefine);
        }

        private static ISet<string> ReadDefines()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var rawDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var symbols = rawDefines
                .Split(DefineSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim())
                .Where(symbol => !string.IsNullOrEmpty(symbol));

            return new HashSet<string>(symbols, StringComparer.Ordinal);
        }

        private static void WriteDefines(ISet<string> defines)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var value = string.Join(";", defines.OrderBy(symbol => symbol, StringComparer.Ordinal));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, value);
        }
    }
}
