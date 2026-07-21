using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(100)]
    internal sealed class PlayServWebSocketTransportModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.TransportWebSocketId,
                "WebSocket",
                "HTTP/WebSocket protocol implementation for ws, wss, http, and https endpoints.",
                PlayServModuleManifest.DefineDisableTransportWebSocket,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Proxy/Modules/WebSocket" },
                dependencyIds: new[] { PlayServModuleManifest.ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null);
        }
    }
}
