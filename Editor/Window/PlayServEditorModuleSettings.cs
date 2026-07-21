using System;
using System.Collections.Generic;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    internal sealed class PlayServEditorModuleSettings
    {
        private const bool DefaultOptionalModuleState = true;
        private const bool DefaultInternalToolState = false;
        private PlayServRuntimeModuleState _runtimeState = new PlayServRuntimeModuleState();

        public bool Deployment { get; private set; } = DefaultOptionalModuleState;
        public bool ModelSync { get; private set; } = DefaultOptionalModuleState;
        public bool Codegen { get; private set; } = DefaultOptionalModuleState;
        public bool ModuleStressTests { get; private set; } = DefaultInternalToolState;
        public bool SdkLogs { get; private set; } = true;

        public void Load()
        {
            Deployment = PlayServEditorModuleAvailability.EditorDeployment &&
                         EditorPrefs.GetBool(Const.PrefModuleDeployment, DefaultOptionalModuleState);
            ModelSync = PlayServEditorModuleAvailability.EditorModelSync &&
                        EditorPrefs.GetBool(Const.PrefModuleModelSync, DefaultOptionalModuleState);
            Codegen = PlayServEditorModuleAvailability.EditorCodegen &&
                      EditorPrefs.GetBool(Const.PrefModuleCodegen, DefaultOptionalModuleState);
            ModuleStressTests = PlayServEditorModuleAvailability.EditorModuleStressTests &&
                                EditorPrefs.GetBool(Const.PrefModuleStressTests, DefaultInternalToolState);
            SdkLogs = PlayServRuntimeModuleDefines.AreSdkLogsEnabled();
            LoadRuntimeModuleDefines();
        }

        public bool SetDeployment(bool enabled) =>
            PlayServEditorModuleAvailability.EditorDeployment &&
            Set(Const.PrefModuleDeployment, Deployment, enabled, value => Deployment = value);

        public bool SetModelSync(bool enabled) =>
            PlayServEditorModuleAvailability.EditorModelSync &&
            Set(Const.PrefModuleModelSync, ModelSync, enabled, value => ModelSync = value);

        public bool SetCodegen(bool enabled) =>
            PlayServEditorModuleAvailability.EditorCodegen &&
            Set(Const.PrefModuleCodegen, Codegen, enabled, value => Codegen = value);

        public bool SetModuleStressTests(bool enabled) =>
            PlayServEditorModuleAvailability.EditorModuleStressTests &&
            Set(Const.PrefModuleStressTests, ModuleStressTests, enabled, value => ModuleStressTests = value);

        public bool SetSdkLogs(bool enabled)
        {
            if (SdkLogs == enabled)
                return false;

            SdkLogs = enabled;
            return PlayServRuntimeModuleDefines.SetSdkLogsEnabled(enabled);
        }

        public bool SetRuntimeModuleEnabled(string moduleId, bool enabled)
        {
            if (!PlayServModuleManifest.TryGet(moduleId, out var module))
                return false;

            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module))
                return false;

            var state = _runtimeState.Clone();
            if (state.IsEnabled(moduleId) == enabled)
                return false;

            if (enabled && TryBuildMissingDependencyList(module, state, out _))
                return false;

            if (!enabled && TryBuildEnabledDependentList(module.Id, state, out _))
                return false;

            state.SetEnabled(moduleId, enabled);
            return ApplyRuntimeState(state);
        }

        public bool CanChangeRuntimeModule(string moduleName)
        {
            if (!TryGetRuntimeModuleByName(moduleName, out var module))
                return false;

            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module))
                return false;

            return _runtimeState.IsEnabled(module.Id)
                ? !TryBuildEnabledDependentList(module.Id, _runtimeState, out _)
                : !TryBuildMissingDependencyList(module, _runtimeState, out _);
        }

        public string GetRuntimeModuleBlockReason(string moduleName)
        {
            if (!TryGetRuntimeModuleByName(moduleName, out var module))
                return string.Empty;

            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module))
                return string.Empty;

            if (_runtimeState.IsEnabled(module.Id) &&
                TryBuildEnabledDependentList(module.Id, _runtimeState, out var dependents))
            {
                return $"Disable dependent modules first: {dependents}.";
            }

            if (!_runtimeState.IsEnabled(module.Id) &&
                TryBuildMissingDependencyList(module, _runtimeState, out var dependencies))
            {
                return $"Enable {dependencies} first.";
            }

            return string.Empty;
        }

        public bool IsRuntimeModuleEnabled(string moduleName)
        {
            if (!TryGetRuntimeModuleByName(moduleName, out var module))
                return false;

            return PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(module) &&
                   _runtimeState.IsEnabled(module.Id);
        }

        public void ResetToDefaults()
        {
            if (PlayServEditorModuleAvailability.EditorDeployment)
                SetDeployment(DefaultOptionalModuleState);

            if (PlayServEditorModuleAvailability.EditorModelSync)
                SetModelSync(DefaultOptionalModuleState);

            if (PlayServEditorModuleAvailability.EditorCodegen)
                SetCodegen(DefaultOptionalModuleState);

            if (PlayServEditorModuleAvailability.EditorModuleStressTests)
                SetModuleStressTests(DefaultInternalToolState);

            SetSdkLogs(true);
            ApplyRuntimeProfile(PlayServSdkProfiles.ClientSdk);
        }

        public bool ApplyRuntimeProfile(PlayServSdkProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var state = new PlayServRuntimeModuleState();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                state.SetEnabled(module.Id, IsProfileModuleEnabled(profile, module.Id));

            return ApplyRuntimeState(state);
        }

        private static bool Set(string key, bool current, bool enabled, Action<bool> assign)
        {
            if (current == enabled)
                return false;

            assign(enabled);
            EditorPrefs.SetBool(key, enabled);
            return true;
        }

        private void LoadRuntimeModuleDefines()
        {
            _runtimeState = PlayServRuntimeModuleDefines.LoadUserPreferenceState();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(_runtimeState);
        }

        private bool ApplyRuntimeState(PlayServRuntimeModuleState state)
        {
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(state);
            PlayServRuntimeModuleDefines.NormalizeDependencies(state);

            var changed = !_runtimeState.HasSameEnabledModules(state);
            _runtimeState = state.Clone();

            return PlayServRuntimeModuleDefines.Apply(state) || changed;
        }

        private static bool IsProfileModuleEnabled(PlayServSdkProfile profile, string moduleId)
        {
            return profile.EnablesModule(moduleId);
        }

        private static bool TryGetRuntimeModuleByName(string moduleName, out PlayServModuleManifestEntry module)
        {
            return PlayServModuleManifest.TryResolve(moduleName, out module);
        }

        private static bool TryBuildMissingDependencyList(
            PlayServModuleManifestEntry module,
            PlayServRuntimeModuleState state,
            out string dependencies)
        {
            var labels = new List<string>();
            for (var i = 0; i < module.DependencyIds.Length; i++)
            {
                if (state.IsEnabled(module.DependencyIds[i]))
                    continue;

                labels.Add(PlayServModuleManifest.GetLabel(module.DependencyIds[i]));
            }

            dependencies = string.Join(", ", labels);
            return labels.Count > 0;
        }

        private static bool TryBuildEnabledDependentList(
            string moduleId,
            PlayServRuntimeModuleState state,
            out string dependents)
        {
            var labels = new List<string>();
            foreach (var module in PlayServModuleManifest.VisibleRuntimeModules)
            {
                if (!state.IsEnabled(module.Id) || !HasDependency(module, moduleId))
                    continue;

                labels.Add(module.Label);
            }

            dependents = string.Join(", ", labels);
            return labels.Count > 0;
        }

        private static bool HasDependency(PlayServModuleManifestEntry module, string dependencyId)
        {
            for (var i = 0; i < module.DependencyIds.Length; i++)
            {
                if (string.Equals(module.DependencyIds[i], dependencyId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
