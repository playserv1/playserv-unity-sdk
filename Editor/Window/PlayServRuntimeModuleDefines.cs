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
            "PLAYSERV_DISABLE_GOOGLE_SIGN_IN",
            "PLAYSERV_DISABLE_TRANSPORT_WEBSOCKET",
            "PLAYSERV_DISABLE_TRANSPORT_UDP",
            "PLAYSERV_DISABLE_TRANSPORT_RUDP",
            "PLAYSERV_DISABLE_TRANSPORT_WEBRTC"
        };

        public static PlayServRuntimeModuleState Load()
        {
            var defines = ReadDefines();
            return CreateRuntimeState(moduleId => IsEnabled(defines, moduleId));
        }

        public static PlayServRuntimeModuleState LoadUserPreferenceState()
        {
            return CreateRuntimeState(IsEnabledByUserPreference);
        }

        public static bool Apply(PlayServRuntimeModuleState state)
        {
            return Apply(state, syncModuleGraph: true);
        }

        internal static bool Apply(PlayServRuntimeModuleState state, bool syncModuleGraph)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            NormalizeDependencies(state);
            WriteUserDisabledPrefs(state);

            var defines = ReadDefines();
            var changed = RemoveLegacyRuntimeModuleDefines(defines);
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                changed |= SetModuleDisabled(defines, module.Id, !state.IsEnabled(module.Id));

            if (!changed)
                return false;

            WriteDefines(defines);
            if (syncModuleGraph)
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

        public static void NormalizeDependencies(PlayServRuntimeModuleState state)
        {
            if (state == null)
                return;

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var module in PlayServModuleManifest.RuntimeModules)
                {
                    if (!state.IsEnabled(module.Id))
                        continue;

                    for (var i = 0; i < module.HiddenDependencyModuleIds.Length; i++)
                    {
                        var dependencyId = module.HiddenDependencyModuleIds[i];
                        if (state.IsEnabled(dependencyId))
                            continue;

                        state.SetEnabled(dependencyId, true);
                        changed = true;
                    }

                    for (var i = 0; i < module.DependencyIds.Length; i++)
                    {
                        if (state.IsEnabled(module.DependencyIds[i]))
                            continue;

                        state.SetEnabled(module.Id, false);
                        changed = true;
                        break;
                    }
                }
            }

            NormalizeHiddenDependencyModules(state);
        }

        private static PlayServRuntimeModuleState CreateRuntimeState(Func<string, bool> isEnabled)
        {
            var state = new PlayServRuntimeModuleState();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                state.SetEnabled(module.Id, isEnabled(module.Id));

            NormalizeDependencies(state);
            return state;
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
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                SetUserModulePreference(module.Id, state.IsEnabled(module.Id));
        }

        private static void NormalizeHiddenDependencyModules(PlayServRuntimeModuleState state)
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (module.VisibleInSettings || !IsReferencedAsHiddenDependency(module.Id))
                    continue;

                state.SetEnabled(module.Id, IsHiddenDependencyRequired(state, module.Id));
            }
        }

        private static bool IsReferencedAsHiddenDependency(string dependencyId)
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (module.HiddenDependencyModuleIds.Contains(dependencyId, StringComparer.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsHiddenDependencyRequired(PlayServRuntimeModuleState state, string dependencyId)
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                if (!state.IsEnabled(module.Id))
                    continue;

                if (module.HiddenDependencyModuleIds.Contains(dependencyId, StringComparer.Ordinal))
                    return true;
            }

            return false;
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
