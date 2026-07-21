using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(120)]
    internal sealed class PlayServRudpTransportModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.TransportRudpId,
                "RUDP",
                "Reliable UDP protocol implementation for native/editor builds.",
                PlayServModuleManifest.DefineDisableTransportRudp,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Proxy/Modules/Rudp" },
                dependencyIds: new[] { PlayServModuleManifest.ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null);
        }
    }
}
