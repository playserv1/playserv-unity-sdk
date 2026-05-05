#if UNITY_EDITOR
using System;
using UnityEditor;

namespace Playserv.Editor
{
    internal sealed class PlayServEditorModuleSettings
    {
        private const bool DefaultOptionalModuleState = true;
        public const string RuntimeModuleEvents = "Events";
        public const string RuntimeModuleData = "Data Subscription";
        public const string RuntimeModuleRpcCore = "RPC Core";
        public const string RuntimeModuleRpc = "RPC";
        public const string RuntimeModuleServerRpc = "Server RPC";
        public const string RuntimeModuleSpawn = "Spawn";
        public const string RuntimeModulePulse = "Pulse";

        public bool Deployment { get; private set; } = DefaultOptionalModuleState;
        public bool ModelSync { get; private set; } = DefaultOptionalModuleState;
        public bool Codegen { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeEvents { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeData { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeRpc { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeServerRpc { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeSpawn { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimePulse { get; private set; } = DefaultOptionalModuleState;

        public void Load()
        {
            Deployment = PlayServEditorModuleAvailability.EditorDeployment &&
                         EditorPrefs.GetBool(Const.PrefModuleDeployment, DefaultOptionalModuleState);
            ModelSync = PlayServEditorModuleAvailability.EditorModelSync &&
                        EditorPrefs.GetBool(Const.PrefModuleModelSync, DefaultOptionalModuleState);
            Codegen = PlayServEditorModuleAvailability.EditorCodegen &&
                      EditorPrefs.GetBool(Const.PrefModuleCodegen, DefaultOptionalModuleState);
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

        public bool SetRuntimeEvents(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeEvents)
                return false;

            if (!enabled && !CanDisableRuntimeEvents)
                return false;

            var state = CreateRuntimeState();
            state.Events = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeData(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeData)
                return false;

            if (enabled && !RuntimeEvents)
                return false;

            var state = CreateRuntimeState();
            state.Data = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeRpc(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeClientRpc)
                return false;

            var state = CreateRuntimeState();
            state.Rpc = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeServerRpc(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeServerRpc)
                return false;

            var state = CreateRuntimeState();
            state.ServerRpc = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeSpawn(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeSpawn)
                return false;

            if (enabled && !RuntimeEvents)
                return false;

            var state = CreateRuntimeState();
            state.Spawn = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimePulse(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimePulse)
                return false;

            var state = CreateRuntimeState();
            state.Pulse = enabled;
            return ApplyRuntimeState(state);
        }

        public bool CanChangeRuntimeModule(string moduleName)
        {
            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(moduleName))
                return false;

            switch (moduleName)
            {
                case RuntimeModuleEvents:
                    return !RuntimeEvents || CanDisableRuntimeEvents;
                case RuntimeModuleData:
                    return RuntimeData || RuntimeEvents;
                case RuntimeModuleSpawn:
                    return RuntimeSpawn || RuntimeEvents;
                default:
                    return true;
            }
        }

        public string GetRuntimeModuleBlockReason(string moduleName)
        {
            switch (moduleName)
            {
                case RuntimeModuleEvents:
                    return RuntimeEvents && !CanDisableRuntimeEvents
                        ? $"Disable dependent modules first: {BuildEnabledDependentsList(RuntimeData, RuntimeSpawn)}."
                        : string.Empty;
                case RuntimeModuleData:
                    return !RuntimeData && !RuntimeEvents ? "Enable Events first." : string.Empty;
                case RuntimeModuleSpawn:
                    return !RuntimeSpawn && !RuntimeEvents ? "Enable Events first." : string.Empty;
                default:
                    return string.Empty;
            }
        }

        public bool IsRuntimeModuleEnabled(string moduleName)
        {
            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(moduleName))
                return false;

            switch (moduleName)
            {
                case RuntimeModuleEvents:
                    return RuntimeEvents;
                case RuntimeModuleData:
                    return RuntimeData;
                case RuntimeModuleRpcCore:
                    return RuntimeRpc || RuntimeServerRpc;
                case RuntimeModuleRpc:
                    return RuntimeRpc;
                case RuntimeModuleServerRpc:
                    return RuntimeServerRpc;
                case RuntimeModuleSpawn:
                    return RuntimeSpawn;
                case RuntimeModulePulse:
                    return RuntimePulse;
                default:
                    return false;
            }
        }

        public void ResetToDefaults()
        {
            if (PlayServEditorModuleAvailability.EditorDeployment)
                SetDeployment(DefaultOptionalModuleState);

            if (PlayServEditorModuleAvailability.EditorModelSync)
                SetModelSync(DefaultOptionalModuleState);

            if (PlayServEditorModuleAvailability.EditorCodegen)
                SetCodegen(DefaultOptionalModuleState);

            ApplyRuntimeState(new PlayServRuntimeModuleState
            {
                Events = PlayServEditorModuleAvailability.RuntimeEvents && DefaultOptionalModuleState,
                Data = PlayServEditorModuleAvailability.RuntimeData && DefaultOptionalModuleState,
                Rpc = PlayServEditorModuleAvailability.RuntimeClientRpc && DefaultOptionalModuleState,
                ServerRpc = PlayServEditorModuleAvailability.RuntimeServerRpc && DefaultOptionalModuleState,
                Spawn = PlayServEditorModuleAvailability.RuntimeSpawn && DefaultOptionalModuleState,
                Pulse = PlayServEditorModuleAvailability.RuntimePulse && DefaultOptionalModuleState
            });
        }

        private static bool Set(string key, bool current, bool enabled, Action<bool> assign)
        {
            if (current == enabled)
                return false;

            assign(enabled);
            EditorPrefs.SetBool(key, enabled);
            return true;
        }

        private bool CanDisableRuntimeEvents => !RuntimeData && !RuntimeSpawn;

        private static string BuildEnabledDependentsList(bool dataEnabled, bool spawnEnabled)
        {
            if (dataEnabled && spawnEnabled)
                return $"{RuntimeModuleData}, {RuntimeModuleSpawn}";

            if (dataEnabled)
                return RuntimeModuleData;

            if (spawnEnabled)
                return RuntimeModuleSpawn;

            return string.Empty;
        }

        private void LoadRuntimeModuleDefines()
        {
            var state = PlayServRuntimeModuleDefines.Load();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(ref state);
            RuntimeEvents = state.Events;
            RuntimeData = state.Data;
            RuntimeRpc = state.Rpc;
            RuntimeServerRpc = state.ServerRpc;
            RuntimeSpawn = state.Spawn;
            RuntimePulse = state.Pulse;
        }

        private PlayServRuntimeModuleState CreateRuntimeState()
        {
            return new PlayServRuntimeModuleState
            {
                Events = RuntimeEvents,
                Data = RuntimeData,
                Rpc = RuntimeRpc,
                ServerRpc = RuntimeServerRpc,
                Spawn = RuntimeSpawn,
                Pulse = RuntimePulse
            };
        }

        private bool ApplyRuntimeState(PlayServRuntimeModuleState state)
        {
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(ref state);
            PlayServRuntimeModuleDefines.NormalizeDependencies(ref state);

            var changed = RuntimeEvents != state.Events ||
                          RuntimeData != state.Data ||
                          RuntimeRpc != state.Rpc ||
                          RuntimeServerRpc != state.ServerRpc ||
                          RuntimeSpawn != state.Spawn ||
                          RuntimePulse != state.Pulse;

            RuntimeEvents = state.Events;
            RuntimeData = state.Data;
            RuntimeRpc = state.Rpc;
            RuntimeServerRpc = state.ServerRpc;
            RuntimeSpawn = state.Spawn;
            RuntimePulse = state.Pulse;

            return PlayServRuntimeModuleDefines.Apply(state) || changed;
        }
    }
}
#endif
