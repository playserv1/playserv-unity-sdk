using System;

namespace Playserv.Modules
{
    internal static class PlayServBuiltInModuleManifest
    {
        public static PlayServModuleManifestEntry[] Build()
        {
            return new[]
            {
                Module("client-execution", 0, "Client Execution", "Client-side runtime execution surface. Disable this for server-only SDK builds.", "PLAYSERV_MODULE_DISABLED_CLIENT_EXECUTION", true, false, true, true, profiles: Values("client-sdk", "full-sdk")),
                Module("events", 10, "Events", "Typed publish/subscribe runtime and typed event API generation controls. Cannot be disabled while dependent modules are enabled.", "PLAYSERV_MODULE_DISABLED_EVENTS", true, false, true, true, Values("Runtime/Modules/Events/Implementation", "Editor/Events"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Modules.Events", profiles: Values("client-sdk", "full-sdk")),
                Module("data-subscription", 20, "Data Subscription", "Shared entity query, mutation, polling, and live subscription APIs.", "PLAYSERV_MODULE_DISABLED_DATA", true, false, true, true, Values("Runtime/Modules/DataSubscription/Implementation"), rootAssemblyReference: "Playserv.Runtime.Modules.DataSubscription", profiles: Values("client-sdk", "full-sdk")),
                Module("rpc-core", 30, "RPC Core", "Shared RPC attributes, payload mapping, and codec helpers.", "PLAYSERV_MODULE_DISABLED_RPC_CORE", true, false, false, false, Values("Runtime/Modules/RPC/Core"), rootAssemblyReference: "Playserv.Runtime.Modules.RPC.Core"),
                Module("client-rpc", 40, "RPC", "Client-side remote RPC commands, generated RPC helpers, and Invoke* wrapper APIs.", "PLAYSERV_MODULE_DISABLED_CLIENT_RPC", true, false, true, true, Values("Runtime/Modules/RPC/Client/Implementation"), Values("client-execution"), Values("Runtime/Modules/RPC/Core"), Values("rpc-core"), "Playserv.Runtime.Modules.RPC.Client", Values("client-sdk", "full-sdk")),
                Module("server", 50, "Server", "Server-side local command/event execution, in-process RPC invoker, service registry, and PlayServServerRpc API.", "PLAYSERV_MODULE_DISABLED_SERVER", false, true, true, true, Values("Runtime/Modules/Server/Implementation"), hiddenDependencyAssetPaths: Values("Runtime/Modules/RPC/Core"), hiddenDependencyModuleIds: Values("rpc-core"), rootAssemblyReference: "Playserv.Runtime.Modules.Server", profiles: Values("server-sdk", "full-sdk")),
                Module("spawn", 60, "Spawn", "NetworkObject/NetworkTransform helpers and Resources-based spawn facade. Depends on Events.", "PLAYSERV_MODULE_DISABLED_SPAWN", true, false, true, true, Values("Runtime/Modules/Spawn/Implementation"), Values("events"), rootAssemblyReference: "Playserv.Runtime.Modules.Spawn", profiles: Values("client-sdk", "full-sdk")),
                Module("pulse", 70, "Pulse", "Realtime config placeholder module and future feature flag surface.", "PLAYSERV_MODULE_DISABLED_PULSE", true, false, true, true, Values("Runtime/Modules/Pulse"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Modules.Pulse", profiles: Values("client-sdk", "full-sdk")),
                Module("apple-sign-in", 80, "Apple Sign In", "Native iOS Sign in with Apple facade, credential settings, Xcode capability setup, quick login, credential state, and revocation callbacks.", "PLAYSERV_MODULE_DISABLED_APPLE_SIGN_IN", false, false, true, true, Values("Runtime/Modules/AppleSignIn/Implementation", "Runtime/Modules/AppleSignIn/Editor"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Modules.AppleSignIn", profiles: Values("full-sdk")),
                Module("google-sign-in", 90, "Google Sign In", "Google Sign-In facade for OAuth ID tokens, server auth codes, profile identity, silent sign-in, sign-out, and disconnect.", "PLAYSERV_MODULE_DISABLED_GOOGLE_SIGN_IN", false, false, true, true, Values("Runtime/Modules/GoogleSignIn/Implementation", "Runtime/Modules/GoogleSignIn/Editor"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Modules.GoogleSignIn", profiles: Values("full-sdk")),
                Module("transport-websocket", 100, "WebSocket", "HTTP/WebSocket protocol implementation for ws, wss, http, and https endpoints.", "PLAYSERV_MODULE_DISABLED_TRANSPORT_WEBSOCKET", true, false, true, true, Values("Runtime/Proxy/Modules/WebSocket"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Transport.WebSocket", profiles: Values("client-sdk", "full-sdk")),
                Module("transport-udp", 110, "UDP", "UDP protocol implementation for native/editor builds.", "PLAYSERV_MODULE_DISABLED_TRANSPORT_UDP", true, false, true, true, Values("Runtime/Proxy/Modules/Udp"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Transport.Udp", profiles: Values("client-sdk", "full-sdk")),
                Module("transport-rudp", 120, "RUDP", "Reliable UDP protocol implementation for native/editor builds.", "PLAYSERV_MODULE_DISABLED_TRANSPORT_RUDP", true, false, true, true, Values("Runtime/Proxy/Modules/Rudp"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Transport.Rudp", profiles: Values("client-sdk", "full-sdk")),
                Module("transport-webrtc", 130, "WebRTC", "WebRTC data channel and signaling implementation.", "PLAYSERV_MODULE_DISABLED_TRANSPORT_WEBRTC", true, false, true, true, Values("Runtime/Proxy/Modules/WebRtc"), Values("client-execution"), rootAssemblyReference: "Playserv.Runtime.Transport.WebRtc", profiles: Values("client-sdk", "full-sdk"))
            };
        }

        private static PlayServModuleManifestEntry Module(
            string id,
            int order,
            string label,
            string description,
            string disableDefine,
            bool defaultEnabled,
            bool isServerModule,
            bool visibleInSettings,
            bool visibleInExport,
            string[] assetPaths = null,
            string[] dependencyIds = null,
            string[] hiddenDependencyAssetPaths = null,
            string[] hiddenDependencyModuleIds = null,
            string rootAssemblyReference = null,
            string[] profiles = null)
        {
            return new PlayServModuleManifestEntry(
                id,
                label,
                description,
                disableDefine,
                defaultEnabled,
                isServerModule,
                visibleInSettings,
                visibleInExport,
                assetPaths,
                dependencyIds,
                hiddenDependencyAssetPaths,
                hiddenDependencyModuleIds,
                rootAssemblyReference,
                order,
                profileIds: profiles);
        }

        private static string[] Values(params string[] values)
        {
            return values ?? Array.Empty<string>();
        }
    }
}
