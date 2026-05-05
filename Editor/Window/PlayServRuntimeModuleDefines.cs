#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Playserv.Editor
{
    internal static class PlayServRuntimeModuleDefines
    {
        private static readonly char[] DefineSeparators = { ';' };

        public static PlayServRuntimeModuleState Load()
        {
            var defines = ReadDefines();
            var rpcCoreDisabled = defines.Contains(Const.DefineDisableRpcCore);
            return new PlayServRuntimeModuleState
            {
                Events = !defines.Contains(Const.DefineDisableEvents),
                Data = !defines.Contains(Const.DefineDisableData) && !defines.Contains(Const.DefineDisableEvents),
                Rpc = !rpcCoreDisabled && !defines.Contains(Const.DefineDisableClientRpc),
                ServerRpc = !rpcCoreDisabled && !defines.Contains(Const.DefineDisableServerRpc),
                Spawn = !defines.Contains(Const.DefineDisableSpawn) && !defines.Contains(Const.DefineDisableEvents),
                Pulse = !defines.Contains(Const.DefineDisablePulse)
            };
        }

        public static bool Apply(PlayServRuntimeModuleState state)
        {
            NormalizeDependencies(ref state);

            var defines = ReadDefines();
            var changed = false;
            var rpcCoreEnabled = state.Rpc || state.ServerRpc;

            changed |= SetDisabled(defines, Const.DefineDisableEvents, !state.Events);
            changed |= SetDisabled(defines, Const.DefineDisableData, !state.Data);
            changed |= SetDisabled(defines, Const.DefineDisableRpcCore, !rpcCoreEnabled);
            changed |= SetDisabled(defines, Const.DefineDisableClientRpc, !state.Rpc);
            changed |= SetDisabled(defines, Const.DefineDisableServerRpc, !state.ServerRpc);
            changed |= SetDisabled(defines, Const.DefineDisableSpawn, !state.Spawn);
            changed |= SetDisabled(defines, Const.DefineDisablePulse, !state.Pulse);

            if (!changed)
                return false;

            WriteDefines(defines);
            return true;
        }

        public static void NormalizeDependencies(ref PlayServRuntimeModuleState state)
        {
            if (state.Events)
                return;

            state.Data = false;
            state.Spawn = false;
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
#endif
