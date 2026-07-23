using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Modules
{
    public static partial class PlayServModuleManifest
    {
        public const string ClientExecutionId = "client-execution";
        public const string EventsId = "events";
        public const string DataSubscriptionId = "data-subscription";
        public const string RpcCoreId = "rpc-core";
        public const string ClientRpcId = "client-rpc";
        public const string ServerId = "server";
        public const string SpawnId = "spawn";
        public const string PulseId = "pulse";
        public const string AppleSignInId = "apple-sign-in";
        public const string GoogleSignInId = "google-sign-in";
        public const string TransportWebSocketId = "transport-websocket";
        public const string TransportUdpId = "transport-udp";
        public const string TransportRudpId = "transport-rudp";
        public const string TransportWebRtcId = "transport-webrtc";

        public const string DefineDisableEvents = "PLAYSERV_MODULE_DISABLED_EVENTS";
        public const string DefineDisableData = "PLAYSERV_MODULE_DISABLED_DATA";
        public const string DefineDisableRpcCore = "PLAYSERV_MODULE_DISABLED_RPC_CORE";
        public const string DefineDisableClientRpc = "PLAYSERV_MODULE_DISABLED_CLIENT_RPC";
        public const string DefineDisableServer = "PLAYSERV_MODULE_DISABLED_SERVER";
        public const string DefineDisableClientExecution = "PLAYSERV_MODULE_DISABLED_CLIENT_EXECUTION";
        public const string DefineDisableSpawn = "PLAYSERV_MODULE_DISABLED_SPAWN";
        public const string DefineDisablePulse = "PLAYSERV_MODULE_DISABLED_PULSE";
        public const string DefineDisableAppleSignIn = "PLAYSERV_MODULE_DISABLED_APPLE_SIGN_IN";
        public const string DefineDisableGoogleSignIn = "PLAYSERV_MODULE_DISABLED_GOOGLE_SIGN_IN";
        public const string DefineDisableTransportWebSocket = "PLAYSERV_MODULE_DISABLED_TRANSPORT_WEBSOCKET";
        public const string DefineDisableTransportUdp = "PLAYSERV_MODULE_DISABLED_TRANSPORT_UDP";
        public const string DefineDisableTransportRudp = "PLAYSERV_MODULE_DISABLED_TRANSPORT_RUDP";
        public const string DefineDisableTransportWebRtc = "PLAYSERV_MODULE_DISABLED_TRANSPORT_WEBRTC";

        private static readonly PlayServModuleManifestEntry[] BuiltInModules = PlayServBuiltInModuleManifest.Build();
        private static PlayServModuleManifestEntry[] _modules = BuiltInModules;

        public static IReadOnlyList<PlayServModuleManifestEntry> RuntimeModules => _modules;

        public static IEnumerable<PlayServModuleManifestEntry> VisibleRuntimeModules
        {
            get
            {
                var modules = _modules;
                for (var i = 0; i < modules.Length; i++)
                {
                    if (modules[i].VisibleInSettings)
                        yield return modules[i];
                }
            }
        }

        public static IEnumerable<PlayServModuleManifestEntry> ExportableRuntimeModules
        {
            get
            {
                var modules = _modules;
                for (var i = 0; i < modules.Length; i++)
                {
                    if (modules[i].VisibleInExport)
                        yield return modules[i];
                }
            }
        }

        public static void SetDiscoveredModules(IEnumerable<PlayServModuleManifestEntry> discoveredModules)
        {
            var discovered = (discoveredModules ?? Array.Empty<PlayServModuleManifestEntry>())
                .Where(module => module != null)
                .ToArray();

            if (discovered.Length == 0)
            {
                _modules = BuiltInModules;
                return;
            }

            _modules = discovered;
        }

        public static bool TryGet(string moduleId, out PlayServModuleManifestEntry module)
        {
            module = null;
            if (string.IsNullOrWhiteSpace(moduleId))
                return false;

            var modules = _modules;
            for (var i = 0; i < modules.Length; i++)
            {
                if (!string.Equals(modules[i].Id, moduleId, StringComparison.Ordinal))
                    continue;

                module = modules[i];
                return true;
            }

            return false;
        }

        public static bool TryResolve(string moduleIdOrLabel, out PlayServModuleManifestEntry module)
        {
            if (TryGet(moduleIdOrLabel, out module))
                return true;

            module = null;
            if (string.IsNullOrWhiteSpace(moduleIdOrLabel))
                return false;

            var modules = _modules;
            for (var i = 0; i < modules.Length; i++)
            {
                if (!string.Equals(modules[i].Label, moduleIdOrLabel, StringComparison.Ordinal))
                    continue;

                module = modules[i];
                return true;
            }

            return false;
        }

        public static PlayServModuleManifestEntry GetRequired(string moduleId)
        {
            if (TryGet(moduleId, out var module))
                return module;

            throw new InvalidOperationException($"Unknown PlayServ module id: {moduleId}");
        }

        public static string GetLabel(string moduleId)
        {
            return TryGet(moduleId, out var module) ? module.Label : moduleId;
        }
    }
}
