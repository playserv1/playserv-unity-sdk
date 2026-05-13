using System;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    internal sealed class PlayServEditorModuleSettings
    {
        private const bool DefaultOptionalModuleState = true;
        private const bool DefaultInternalToolState = false;
        public const string RuntimeModuleEvents = "Events";
        public const string RuntimeModuleData = "Data Subscription";
        public const string RuntimeModuleRpcCore = "RPC Core";
        public const string RuntimeModuleRpc = "RPC";
        public const string RuntimeModuleServer = "Server";
        public const string RuntimeModuleClientExecution = "Client Execution";
        public const string RuntimeModuleSpawn = "Spawn";
        public const string RuntimeModulePulse = "Pulse";
        public const string RuntimeModuleTransportWebSocket = "WebSocket";
        public const string RuntimeModuleTransportUdp = "UDP";
        public const string RuntimeModuleTransportRudp = "RUDP";
        public const string RuntimeModuleTransportWebRtc = "WebRTC";

        public bool Deployment { get; private set; } = DefaultOptionalModuleState;
        public bool ModelSync { get; private set; } = DefaultOptionalModuleState;
        public bool Codegen { get; private set; } = DefaultOptionalModuleState;
        public bool ModuleStressTests { get; private set; } = DefaultInternalToolState;
        public bool RuntimeEvents { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeData { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeRpc { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeServer { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeClientExecution { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeSpawn { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimePulse { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeTransportWebSocket { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeTransportUdp { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeTransportRudp { get; private set; } = DefaultOptionalModuleState;
        public bool RuntimeTransportWebRtc { get; private set; } = DefaultOptionalModuleState;
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

        public bool SetRuntimeEvents(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeEvents)
                return false;

            if (enabled && !RuntimeClientExecution)
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

            var state = CreateRuntimeState();
            state.Data = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeRpc(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeClientRpc)
                return false;

            if (enabled && !RuntimeClientExecution)
                return false;

            var state = CreateRuntimeState();
            state.Rpc = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeServer(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeServer)
                return false;

            var state = CreateRuntimeState();
            state.Server = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeClientExecution(bool enabled)
        {
            if (!PlayServEditorModuleAvailability.RuntimeClientExecution)
                return false;

            if (!enabled && !CanDisableRuntimeClientExecution)
                return false;

            var state = CreateRuntimeState();
            state.ClientExecution = enabled;
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

            if (enabled && !RuntimeClientExecution)
                return false;

            var state = CreateRuntimeState();
            state.Pulse = enabled;
            return ApplyRuntimeState(state);
        }

        public bool SetRuntimeTransportWebSocket(bool enabled) =>
            SetRuntimeTransportModule(
                enabled,
                PlayServEditorModuleAvailability.RuntimeTransportWebSocket,
                PlayServModuleManifest.TransportWebSocketId);

        public bool SetRuntimeTransportUdp(bool enabled) =>
            SetRuntimeTransportModule(
                enabled,
                PlayServEditorModuleAvailability.RuntimeTransportUdp,
                PlayServModuleManifest.TransportUdpId);

        public bool SetRuntimeTransportRudp(bool enabled) =>
            SetRuntimeTransportModule(
                enabled,
                PlayServEditorModuleAvailability.RuntimeTransportRudp,
                PlayServModuleManifest.TransportRudpId);

        public bool SetRuntimeTransportWebRtc(bool enabled) =>
            SetRuntimeTransportModule(
                enabled,
                PlayServEditorModuleAvailability.RuntimeTransportWebRtc,
                PlayServModuleManifest.TransportWebRtcId);

        public bool CanChangeRuntimeModule(string moduleName)
        {
            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(moduleName))
                return false;

            switch (moduleName)
            {
                case RuntimeModuleClientExecution:
                    return !RuntimeClientExecution || CanDisableRuntimeClientExecution;
                case RuntimeModuleEvents:
                    return RuntimeEvents ? CanDisableRuntimeEvents : RuntimeClientExecution;
                case RuntimeModuleData:
                    return true;
                case RuntimeModuleRpc:
                    return RuntimeRpc || RuntimeClientExecution;
                case RuntimeModuleServer:
                    return true;
                case RuntimeModuleSpawn:
                    return RuntimeSpawn || RuntimeEvents;
                case RuntimeModulePulse:
                    return RuntimePulse || RuntimeClientExecution;
                case RuntimeModuleTransportWebSocket:
                    return RuntimeTransportWebSocket || RuntimeClientExecution;
                case RuntimeModuleTransportUdp:
                    return RuntimeTransportUdp || RuntimeClientExecution;
                case RuntimeModuleTransportRudp:
                    return RuntimeTransportRudp || RuntimeClientExecution;
                case RuntimeModuleTransportWebRtc:
                    return RuntimeTransportWebRtc || RuntimeClientExecution;
                default:
                    return true;
            }
        }

        public string GetRuntimeModuleBlockReason(string moduleName)
        {
            switch (moduleName)
            {
                case RuntimeModuleClientExecution:
                    return RuntimeClientExecution && !CanDisableRuntimeClientExecution
                        ? $"Disable dependent modules first: {BuildEnabledClientDependentsList()}."
                        : string.Empty;
                case RuntimeModuleEvents:
                    return RuntimeEvents && !CanDisableRuntimeEvents
                        ? $"Disable dependent modules first: {BuildEnabledDependentsList(RuntimeSpawn)}."
                        : string.Empty;
                case RuntimeModuleData:
                    return string.Empty;
                case RuntimeModuleRpc:
                    return !RuntimeRpc && !RuntimeClientExecution ? "Enable Client Execution first." : string.Empty;
                case RuntimeModuleServer:
                    return string.Empty;
                case RuntimeModuleSpawn:
                    return !RuntimeSpawn && !RuntimeEvents ? "Enable Events first." : string.Empty;
                case RuntimeModulePulse:
                    return !RuntimePulse && !RuntimeClientExecution ? "Enable Client Execution first." : string.Empty;
                case RuntimeModuleTransportWebSocket:
                    return !RuntimeTransportWebSocket && !RuntimeClientExecution ? "Enable Client Execution first." : string.Empty;
                case RuntimeModuleTransportUdp:
                    return !RuntimeTransportUdp && !RuntimeClientExecution ? "Enable Client Execution first." : string.Empty;
                case RuntimeModuleTransportRudp:
                    return !RuntimeTransportRudp && !RuntimeClientExecution ? "Enable Client Execution first." : string.Empty;
                case RuntimeModuleTransportWebRtc:
                    return !RuntimeTransportWebRtc && !RuntimeClientExecution ? "Enable Client Execution first." : string.Empty;
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
                    return RuntimeRpc || RuntimeServer;
                case RuntimeModuleRpc:
                    return RuntimeRpc;
                case RuntimeModuleServer:
                    return RuntimeServer;
                case RuntimeModuleClientExecution:
                    return RuntimeClientExecution;
                case RuntimeModuleSpawn:
                    return RuntimeSpawn;
                case RuntimeModulePulse:
                    return RuntimePulse;
                case RuntimeModuleTransportWebSocket:
                    return RuntimeTransportWebSocket;
                case RuntimeModuleTransportUdp:
                    return RuntimeTransportUdp;
                case RuntimeModuleTransportRudp:
                    return RuntimeTransportRudp;
                case RuntimeModuleTransportWebRtc:
                    return RuntimeTransportWebRtc;
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

            if (PlayServEditorModuleAvailability.EditorModuleStressTests)
                SetModuleStressTests(DefaultInternalToolState);

            SetSdkLogs(true);
            ApplyRuntimeProfile(PlayServSdkProfiles.ClientSdk);
        }

        public bool ApplyRuntimeProfile(PlayServSdkProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            return ApplyRuntimeState(new PlayServRuntimeModuleState
            {
                Events = IsProfileModuleEnabled(profile, PlayServModuleManifest.EventsId),
                Data = IsProfileModuleEnabled(profile, PlayServModuleManifest.DataSubscriptionId),
                Rpc = IsProfileModuleEnabled(profile, PlayServModuleManifest.ClientRpcId),
                Server = IsProfileModuleEnabled(profile, PlayServModuleManifest.ServerId),
                ClientExecution = IsProfileModuleEnabled(profile, PlayServModuleManifest.ClientExecutionId),
                Spawn = IsProfileModuleEnabled(profile, PlayServModuleManifest.SpawnId),
                Pulse = IsProfileModuleEnabled(profile, PlayServModuleManifest.PulseId),
                TransportWebSocket = IsProfileModuleEnabled(profile, PlayServModuleManifest.TransportWebSocketId),
                TransportUdp = IsProfileModuleEnabled(profile, PlayServModuleManifest.TransportUdpId),
                TransportRudp = IsProfileModuleEnabled(profile, PlayServModuleManifest.TransportRudpId),
                TransportWebRtc = IsProfileModuleEnabled(profile, PlayServModuleManifest.TransportWebRtcId)
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

        private bool CanDisableRuntimeEvents => !RuntimeSpawn;

        private bool CanDisableRuntimeClientExecution =>
            !RuntimeEvents &&
            !RuntimeRpc &&
            !RuntimePulse &&
            !RuntimeTransportWebSocket &&
            !RuntimeTransportUdp &&
            !RuntimeTransportRudp &&
            !RuntimeTransportWebRtc;

        private static string BuildEnabledDependentsList(bool spawnEnabled)
        {
            if (spawnEnabled)
                return RuntimeModuleSpawn;

            return string.Empty;
        }

        private string BuildEnabledClientDependentsList()
        {
            var result = string.Empty;

            AppendEnabledModule(ref result, RuntimeEvents, RuntimeModuleEvents);
            AppendEnabledModule(ref result, RuntimeRpc, RuntimeModuleRpc);
            AppendEnabledModule(ref result, RuntimePulse, RuntimeModulePulse);
            AppendEnabledModule(ref result, RuntimeTransportWebSocket, RuntimeModuleTransportWebSocket);
            AppendEnabledModule(ref result, RuntimeTransportUdp, RuntimeModuleTransportUdp);
            AppendEnabledModule(ref result, RuntimeTransportRudp, RuntimeModuleTransportRudp);
            AppendEnabledModule(ref result, RuntimeTransportWebRtc, RuntimeModuleTransportWebRtc);

            return result;
        }

        private static void AppendEnabledModule(ref string result, bool enabled, string moduleName)
        {
            if (!enabled)
                return;

            result = string.IsNullOrEmpty(result)
                ? moduleName
                : $"{result}, {moduleName}";
        }

        private void LoadRuntimeModuleDefines()
        {
            var state = PlayServRuntimeModuleDefines.LoadUserPreferenceState();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(ref state);
            RuntimeEvents = state.Events;
            RuntimeData = state.Data;
            RuntimeRpc = state.Rpc;
            RuntimeServer = state.Server;
            RuntimeClientExecution = state.ClientExecution;
            RuntimeSpawn = state.Spawn;
            RuntimePulse = state.Pulse;
            RuntimeTransportWebSocket = state.TransportWebSocket;
            RuntimeTransportUdp = state.TransportUdp;
            RuntimeTransportRudp = state.TransportRudp;
            RuntimeTransportWebRtc = state.TransportWebRtc;
        }

        private PlayServRuntimeModuleState CreateRuntimeState()
        {
            return new PlayServRuntimeModuleState
            {
                Events = RuntimeEvents,
                Data = RuntimeData,
                Rpc = RuntimeRpc,
                Server = RuntimeServer,
                ClientExecution = RuntimeClientExecution,
                Spawn = RuntimeSpawn,
                Pulse = RuntimePulse,
                TransportWebSocket = RuntimeTransportWebSocket,
                TransportUdp = RuntimeTransportUdp,
                TransportRudp = RuntimeTransportRudp,
                TransportWebRtc = RuntimeTransportWebRtc
            };
        }

        private bool SetRuntimeTransportModule(
            bool enabled,
            bool available,
            string moduleId)
        {
            if (!available)
                return false;

            if (enabled && !RuntimeClientExecution)
                return false;

            var state = CreateRuntimeState();
            switch (moduleId)
            {
                case PlayServModuleManifest.TransportWebSocketId:
                    state.TransportWebSocket = enabled;
                    break;
                case PlayServModuleManifest.TransportUdpId:
                    state.TransportUdp = enabled;
                    break;
                case PlayServModuleManifest.TransportRudpId:
                    state.TransportRudp = enabled;
                    break;
                case PlayServModuleManifest.TransportWebRtcId:
                    state.TransportWebRtc = enabled;
                    break;
                default:
                    return false;
            }

            return ApplyRuntimeState(state);
        }

        private bool ApplyRuntimeState(PlayServRuntimeModuleState state)
        {
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(ref state);
            PlayServRuntimeModuleDefines.NormalizeDependencies(ref state);

            var changed = RuntimeEvents != state.Events ||
                          RuntimeData != state.Data ||
                          RuntimeRpc != state.Rpc ||
                          RuntimeServer != state.Server ||
                          RuntimeClientExecution != state.ClientExecution ||
                          RuntimeSpawn != state.Spawn ||
                          RuntimePulse != state.Pulse ||
                          RuntimeTransportWebSocket != state.TransportWebSocket ||
                          RuntimeTransportUdp != state.TransportUdp ||
                          RuntimeTransportRudp != state.TransportRudp ||
                          RuntimeTransportWebRtc != state.TransportWebRtc;

            RuntimeEvents = state.Events;
            RuntimeData = state.Data;
            RuntimeRpc = state.Rpc;
            RuntimeServer = state.Server;
            RuntimeClientExecution = state.ClientExecution;
            RuntimeSpawn = state.Spawn;
            RuntimePulse = state.Pulse;
            RuntimeTransportWebSocket = state.TransportWebSocket;
            RuntimeTransportUdp = state.TransportUdp;
            RuntimeTransportRudp = state.TransportRudp;
            RuntimeTransportWebRtc = state.TransportWebRtc;

            return PlayServRuntimeModuleDefines.Apply(state) || changed;
        }

        private static bool IsProfileModuleEnabled(PlayServSdkProfile profile, string moduleId)
        {
            return profile.EnablesModule(moduleId);
        }
    }
}
