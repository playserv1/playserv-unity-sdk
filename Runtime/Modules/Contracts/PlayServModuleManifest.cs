using System;
using System.Collections.Generic;

namespace Playserv.Modules
{
    public static class PlayServModuleManifest
    {
        public const string ClientExecutionId = "client-execution";
        public const string EventsId = "events";
        public const string DataSubscriptionId = "data-subscription";
        public const string RpcCoreId = "rpc-core";
        public const string ClientRpcId = "client-rpc";
        public const string ServerId = "server";
        public const string SpawnId = "spawn";
        public const string PulseId = "pulse";

        public const string DefineDisableEvents = "PLAYSERV_MODULE_DISABLED_EVENTS";
        public const string DefineDisableData = "PLAYSERV_MODULE_DISABLED_DATA";
        public const string DefineDisableRpcCore = "PLAYSERV_MODULE_DISABLED_RPC_CORE";
        public const string DefineDisableClientRpc = "PLAYSERV_MODULE_DISABLED_CLIENT_RPC";
        public const string DefineDisableServer = "PLAYSERV_MODULE_DISABLED_SERVER";
        public const string DefineDisableClientExecution = "PLAYSERV_MODULE_DISABLED_CLIENT_EXECUTION";
        public const string DefineDisableSpawn = "PLAYSERV_MODULE_DISABLED_SPAWN";
        public const string DefineDisablePulse = "PLAYSERV_MODULE_DISABLED_PULSE";

        private static readonly PlayServModuleManifestEntry[] Modules =
        {
            new PlayServModuleManifestEntry(
                ClientExecutionId,
                "Client Execution",
                "Client-side transport execution surface. Disable this for server-only SDK builds.",
                DefineDisableClientExecution,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: null,
                dependencyIds: null,
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null),

            new PlayServModuleManifestEntry(
                EventsId,
                "Events",
                "Typed publish/subscribe runtime and typed event API generation controls. Cannot be disabled while dependent modules are enabled.",
                DefineDisableEvents,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Events/Implementation", "Editor/Events" },
                dependencyIds: new[] { ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null),

            new PlayServModuleManifestEntry(
                DataSubscriptionId,
                "Data Subscription",
                "Shared entity query, mutation, polling, and transport subscription APIs.",
                DefineDisableData,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/DataSubscription/Implementation" },
                dependencyIds: null,
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null),

            new PlayServModuleManifestEntry(
                RpcCoreId,
                "RPC Core",
                "Shared RPC attributes, payload mapping, and codec helpers.",
                DefineDisableRpcCore,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: false,
                visibleInExport: false,
                assetPaths: new[] { "Runtime/Modules/RPC/Core" },
                dependencyIds: null,
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null),

            new PlayServModuleManifestEntry(
                ClientRpcId,
                "RPC",
                "Client-side remote RPC commands, generated RPC helpers, and Invoke* wrapper APIs.",
                DefineDisableClientRpc,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/RPC/Client/Implementation" },
                dependencyIds: new[] { ClientExecutionId },
                hiddenDependencyAssetPaths: new[] { "Runtime/Modules/RPC/Core" },
                hiddenDependencyModuleIds: new[] { RpcCoreId }),

            new PlayServModuleManifestEntry(
                ServerId,
                "Server",
                "Server-side local command/event execution, in-process RPC invoker, service registry, and PlayServServerRpc API.",
                DefineDisableServer,
                defaultEnabled: false,
                isServerModule: true,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Server/Implementation" },
                dependencyIds: null,
                hiddenDependencyAssetPaths: new[]
                {
                    "Runtime/Modules/RPC/Core"
                },
                hiddenDependencyModuleIds: new[] { RpcCoreId }),

            new PlayServModuleManifestEntry(
                SpawnId,
                "Spawn",
                "NetworkObject/NetworkTransform helpers and Resources-based spawn facade. Depends on Events.",
                DefineDisableSpawn,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Spawn/Implementation" },
                dependencyIds: new[] { EventsId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null),

            new PlayServModuleManifestEntry(
                PulseId,
                "Pulse",
                "Realtime config placeholder module and future feature flag surface.",
                DefineDisablePulse,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Pulse" },
                dependencyIds: new[] { ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null)
        };

        public static IReadOnlyList<PlayServModuleManifestEntry> RuntimeModules => Modules;

        public static IEnumerable<PlayServModuleManifestEntry> VisibleRuntimeModules
        {
            get
            {
                for (var i = 0; i < Modules.Length; i++)
                {
                    if (Modules[i].VisibleInSettings)
                        yield return Modules[i];
                }
            }
        }

        public static IEnumerable<PlayServModuleManifestEntry> ExportableRuntimeModules
        {
            get
            {
                for (var i = 0; i < Modules.Length; i++)
                {
                    if (Modules[i].VisibleInExport)
                        yield return Modules[i];
                }
            }
        }

        public static bool TryGet(string moduleId, out PlayServModuleManifestEntry module)
        {
            module = null;
            if (string.IsNullOrWhiteSpace(moduleId))
                return false;

            for (var i = 0; i < Modules.Length; i++)
            {
                if (!string.Equals(Modules[i].Id, moduleId, StringComparison.Ordinal))
                    continue;

                module = Modules[i];
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
