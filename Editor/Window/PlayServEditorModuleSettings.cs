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
        public const string RuntimeModuleRpc = "RPC";
        public const string RuntimeModuleSpawn = "Spawn";
        public const string RuntimeModulePulse = "Pulse";

        public bool Deployment { get; private set; } = DefaultOptionalModuleState;
        public bool ModelSync { get; private set; } = DefaultOptionalModuleState;
        public bool Events { get; private set; } = DefaultOptionalModuleState;
        public bool Codegen { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeEvents { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeData { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeRpc { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeSpawn { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimePulse { get; private set; } = DefaultOptionalModuleState;

        public void Load()
        {
            Deployment = EditorPrefs.GetBool(Const.PrefModuleDeployment, DefaultOptionalModuleState);
            ModelSync = EditorPrefs.GetBool(Const.PrefModuleModelSync, DefaultOptionalModuleState);
            Events = EditorPrefs.GetBool(Const.PrefModuleEvents, DefaultOptionalModuleState);
            Codegen = EditorPrefs.GetBool(Const.PrefModuleCodegen, DefaultOptionalModuleState);
            LoadRuntimeModuleDefines();
        }

        public bool SetDeployment(bool enabled) => Set(Const.PrefModuleDeployment, Deployment, enabled, value => Deployment = value);

        public bool SetModelSync(bool enabled) => Set(Const.PrefModuleModelSync, ModelSync, enabled, value => ModelSync = value);

        public bool SetEvents(bool enabled) => Set(Const.PrefModuleEvents, Events, enabled, value => Events = value);

        public bool SetCodegen(bool enabled) => Set(Const.PrefModuleCodegen, Codegen, enabled, value => Codegen = value);

        public bool SetRuntimeEvents(bool enabled)
        {
            if (!enabled && !CanDisableRuntimeEvents)
                return false;

            var state = CreateRuntimeState();
            state.Events = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeData(bool enabled)
        {
            if (enabled && !RuntimeEvents)
                return false;

            var state = CreateRuntimeState();
            state.Data = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeRpc(bool enabled)
        {
            var state = CreateRuntimeState();
            state.Rpc = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeSpawn(bool enabled)
        {
            if (enabled && !RuntimeEvents)
                return false;

            var state = CreateRuntimeState();
            state.Spawn = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimePulse(bool enabled)
        {
            var state = CreateRuntimeState();
            state.Pulse = enabled;
            return ApplyRuntimeState(state);
        }

        public bool CanChangeRuntimeModule(string moduleName)
        {
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
            switch (moduleName)
            {
                case RuntimeModuleEvents:
                    return RuntimeEvents;
                case RuntimeModuleData:
                    return RuntimeData;
                case RuntimeModuleRpc:
                    return RuntimeRpc;
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
            SetDeployment(DefaultOptionalModuleState);
            SetModelSync(DefaultOptionalModuleState);
            SetEvents(DefaultOptionalModuleState);
            SetCodegen(DefaultOptionalModuleState);
            ApplyRuntimeState(new PlayServRuntimeModuleState
            {
                Events = DefaultOptionalModuleState,
                Data = DefaultOptionalModuleState,
                Rpc = DefaultOptionalModuleState,
                Spawn = DefaultOptionalModuleState,
                Pulse = DefaultOptionalModuleState
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
            RuntimeEvents = state.Events;
            RuntimeData = state.Data;
            RuntimeRpc = state.Rpc;
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
                Spawn = RuntimeSpawn,
                Pulse = RuntimePulse
            };
        }

        private bool ApplyRuntimeState(PlayServRuntimeModuleState state)
        {
            PlayServRuntimeModuleDefines.NormalizeDependencies(ref state);

            var changed = RuntimeEvents != state.Events ||
                          RuntimeData != state.Data ||
                          RuntimeRpc != state.Rpc ||
                          RuntimeSpawn != state.Spawn ||
                          RuntimePulse != state.Pulse;

            RuntimeEvents = state.Events;
            RuntimeData = state.Data;
            RuntimeRpc = state.Rpc;
            RuntimeSpawn = state.Spawn;
            RuntimePulse = state.Pulse;

            return PlayServRuntimeModuleDefines.Apply(state) || changed;
        }
    }
}
#endif
