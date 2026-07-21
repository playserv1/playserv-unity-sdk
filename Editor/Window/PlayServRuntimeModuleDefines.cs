using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    internal static class PlayServRuntimeModuleDefines
    {
        public const string SdkLogsDisabledDefine = "PLAYSERV_DISABLE_LOGS";
        private static readonly char[] DefineSeparators = { ';' };
        private static readonly string[] LegacyRuntimeModuleDisableDefines =
        {
            "PLAYSERV_DISABLE_EVENTS",
            "PLAYSERV_DISABLE_DATA",
            "PLAYSERV_DISABLE_RPC_CORE",
            "PLAYSERV_DISABLE_CLIENT_RPC",
            "PLAYSERV_DISABLE_SERVER_RPC",
            "PLAYSERV_MODULE_DISABLED_SERVER_RPC",
            "PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE",
            "PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE",
            "PLAYSERV_DISABLE_CLIENT_EXECUTION",
            "PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER",
            "PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER",
            "PLAYSERV_DISABLE_SPAWN",
            "PLAYSERV_DISABLE_PULSE",
            "PLAYSERV_DISABLE_APPLE_SIGN_IN",
            "PLAYSERV_DISABLE_TRANSPORT_WEBSOCKET",
            "PLAYSERV_DISABLE_TRANSPORT_UDP",
            "PLAYSERV_DISABLE_TRANSPORT_RUDP",
            "PLAYSERV_DISABLE_TRANSPORT_WEBRTC"
        };

        public static PlayServRuntimeModuleState Load()
        {
            var defines = ReadDefines();
            var rpcCoreDisabled = IsDisabled(defines, PlayServModuleManifest.RpcCoreId);
            var state = new PlayServRuntimeModuleState
            {
                Events = IsEnabled(defines, PlayServModuleManifest.EventsId),
                Data = IsEnabled(defines, PlayServModuleManifest.DataSubscriptionId),
                Rpc = !rpcCoreDisabled && IsEnabled(defines, PlayServModuleManifest.ClientRpcId),
                Server = !rpcCoreDisabled && IsEnabled(defines, PlayServModuleManifest.ServerId),
                ClientExecution = IsEnabled(defines, PlayServModuleManifest.ClientExecutionId),
                Spawn = IsEnabled(defines, PlayServModuleManifest.SpawnId) &&
                        IsEnabled(defines, PlayServModuleManifest.EventsId),
                Pulse = IsEnabled(defines, PlayServModuleManifest.PulseId),
                AppleSignIn = IsEnabled(defines, PlayServModuleManifest.AppleSignInId),
                TransportWebSocket = IsEnabled(defines, PlayServModuleManifest.TransportWebSocketId),
                TransportUdp = IsEnabled(defines, PlayServModuleManifest.TransportUdpId),
                TransportRudp = IsEnabled(defines, PlayServModuleManifest.TransportRudpId),
                TransportWebRtc = IsEnabled(defines, PlayServModuleManifest.TransportWebRtcId)
            };
            NormalizeDependencies(ref state);
            return state;
        }

        public static PlayServRuntimeModuleState LoadUserPreferenceState()
        {
            var rpcCoreEnabled = IsEnabledByUserPreference(PlayServModuleManifest.RpcCoreId);
            var eventsEnabled = IsEnabledByUserPreference(PlayServModuleManifest.EventsId);
            var state = new PlayServRuntimeModuleState
            {
                Events = eventsEnabled,
                Data = IsEnabledByUserPreference(PlayServModuleManifest.DataSubscriptionId),
                Rpc = rpcCoreEnabled && IsEnabledByUserPreference(PlayServModuleManifest.ClientRpcId),
                Server = rpcCoreEnabled && IsEnabledByUserPreference(PlayServModuleManifest.ServerId),
                ClientExecution = IsEnabledByUserPreference(PlayServModuleManifest.ClientExecutionId),
                Spawn = eventsEnabled && IsEnabledByUserPreference(PlayServModuleManifest.SpawnId),
                Pulse = IsEnabledByUserPreference(PlayServModuleManifest.PulseId),
                AppleSignIn = IsEnabledByUserPreference(PlayServModuleManifest.AppleSignInId),
                TransportWebSocket = IsEnabledByUserPreference(PlayServModuleManifest.TransportWebSocketId),
                TransportUdp = IsEnabledByUserPreference(PlayServModuleManifest.TransportUdpId),
                TransportRudp = IsEnabledByUserPreference(PlayServModuleManifest.TransportRudpId),
                TransportWebRtc = IsEnabledByUserPreference(PlayServModuleManifest.TransportWebRtcId)
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
            var rpcCoreEnabled = state.Rpc || state.Server;

            changed |= SetModuleDisabled(defines, PlayServModuleManifest.EventsId, !state.Events);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.DataSubscriptionId, !state.Data);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.RpcCoreId, !rpcCoreEnabled);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.ClientRpcId, !state.Rpc);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.ServerId, !state.Server);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.ClientExecutionId, !state.ClientExecution);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.SpawnId, !state.Spawn);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.PulseId, !state.Pulse);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.AppleSignInId, !state.AppleSignIn);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.TransportWebSocketId, !state.TransportWebSocket);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.TransportUdpId, !state.TransportUdp);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.TransportRudpId, !state.TransportRudp);
            changed |= SetModuleDisabled(defines, PlayServModuleManifest.TransportWebRtcId, !state.TransportWebRtc);

            if (!changed)
                return false;

            WriteDefines(defines);
            PlayServModuleGraphSynchronizer.SyncNow();
            return true;
        }

        public static bool IsUserDisabled(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId))
                return false;

            return EditorPrefs.GetBool(BuildUserDisabledPrefKey(moduleId), false);
        }

        public static bool IsModuleEnabled(ISet<string> defines, string moduleId)
        {
            return IsEnabled(defines, moduleId);
        }

        public static bool IsEnabledByUserPreference(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId))
                return false;

            var module = PlayServModuleManifest.GetRequired(moduleId);
            if (EditorPrefs.GetBool(BuildUserEnabledPrefKey(moduleId), false))
                return true;

            if (IsUserDisabled(moduleId))
                return false;

            return module.DefaultEnabled;
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

        public static bool AreSdkLogsEnabled()
        {
            return !ReadDefines().Contains(SdkLogsDisabledDefine);
        }

        public static bool SetSdkLogsEnabled(bool enabled)
        {
            var defines = ReadDefines();
            var changed = enabled
                ? defines.Remove(SdkLogsDisabledDefine)
                : defines.Add(SdkLogsDisabledDefine);

            if (!changed)
                return false;

            WriteDefines(defines);
            return true;
        }

        public static bool RestoreDefaultEnabledModules(IEnumerable<string> moduleIds)
        {
            if (moduleIds == null)
                return false;

            var defines = ReadDefines();
            var changed = false;

            foreach (var moduleId in moduleIds)
            {
                if (!PlayServModuleManifest.TryGet(moduleId, out var module) || !module.DefaultEnabled)
                    continue;

                EditorPrefs.DeleteKey(BuildUserDisabledPrefKey(moduleId));
                EditorPrefs.DeleteKey(BuildUserEnabledPrefKey(moduleId));
                changed |= defines.Remove(module.DisableDefine);
            }

            if (!changed)
                return false;

            WriteDefines(defines);
            return true;
        }

        public static bool RemoveStaleDefaultDisableDefines(IEnumerable<string> moduleIds)
        {
            if (moduleIds == null)
                return false;

            var defines = ReadDefines();
            var changed = false;

            foreach (var moduleId in moduleIds)
            {
                if (!PlayServModuleManifest.TryGet(moduleId, out var module) || !module.DefaultEnabled)
                    continue;

                if (IsUserDisabled(moduleId))
                    continue;

                changed |= defines.Remove(module.DisableDefine);
            }

            if (!changed)
                return false;

            WriteDefines(defines);
            return true;
        }

        public static void NormalizeDependencies(ref PlayServRuntimeModuleState state)
        {
            if (!state.ClientExecution)
            {
                state.Events = false;
                state.Rpc = false;
                state.Spawn = false;
                state.Pulse = false;
                state.AppleSignIn = false;
                state.TransportWebSocket = false;
                state.TransportUdp = false;
                state.TransportRudp = false;
                state.TransportWebRtc = false;
            }

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
            var rpcCoreEnabled = state.Rpc || state.Server;

            SetUserModulePreference(PlayServModuleManifest.EventsId, state.Events);
            SetUserModulePreference(PlayServModuleManifest.DataSubscriptionId, state.Data);
            SetUserModulePreference(PlayServModuleManifest.RpcCoreId, rpcCoreEnabled);
            SetUserModulePreference(PlayServModuleManifest.ClientRpcId, state.Rpc);
            SetUserModulePreference(PlayServModuleManifest.ServerId, state.Server);
            SetUserModulePreference(PlayServModuleManifest.ClientExecutionId, state.ClientExecution);
            SetUserModulePreference(PlayServModuleManifest.SpawnId, state.Spawn);
            SetUserModulePreference(PlayServModuleManifest.PulseId, state.Pulse);
            SetUserModulePreference(PlayServModuleManifest.AppleSignInId, state.AppleSignIn);
            SetUserModulePreference(PlayServModuleManifest.TransportWebSocketId, state.TransportWebSocket);
            SetUserModulePreference(PlayServModuleManifest.TransportUdpId, state.TransportUdp);
            SetUserModulePreference(PlayServModuleManifest.TransportRudpId, state.TransportRudp);
            SetUserModulePreference(PlayServModuleManifest.TransportWebRtcId, state.TransportWebRtc);
        }

        private static void SetUserModulePreference(string moduleId, bool enabled)
        {
            var module = PlayServModuleManifest.GetRequired(moduleId);
            var disabledKey = BuildUserDisabledPrefKey(moduleId);
            var enabledKey = BuildUserEnabledPrefKey(moduleId);

            EditorPrefs.DeleteKey(disabledKey);
            EditorPrefs.DeleteKey(enabledKey);

            if (enabled == module.DefaultEnabled)
                return;

            EditorPrefs.SetBool(enabled ? enabledKey : disabledKey, true);
        }

        private static string BuildUserDisabledPrefKey(string moduleId)
        {
            return Const.PrefRuntimeModuleUserDisabledPrefix + moduleId;
        }

        private static string BuildUserEnabledPrefKey(string moduleId)
        {
            return $"{Const.PrefRuntimeModuleUserDisabledPrefix}enabled.{moduleId}";
        }

        private static bool IsEnabled(ISet<string> defines, string moduleId)
        {
            if (IsDisabled(defines, moduleId))
                return false;

            var module = PlayServModuleManifest.GetRequired(moduleId);
            if (module.DefaultEnabled)
                return true;

            return EditorPrefs.GetBool(BuildUserEnabledPrefKey(moduleId), false);
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
