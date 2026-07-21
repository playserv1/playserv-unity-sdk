using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(50)]
    internal sealed class PlayServServerModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.ServerId,
                "Server",
                "Server-side local command/event execution, in-process RPC invoker, service registry, and PlayServServerRpc API.",
                PlayServModuleManifest.DefineDisableServer,
                defaultEnabled: false,
                isServerModule: true,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Server/Implementation" },
                dependencyIds: null,
                hiddenDependencyAssetPaths: new[] { "Runtime/Modules/RPC/Core" },
                hiddenDependencyModuleIds: new[] { PlayServModuleManifest.RpcCoreId },
                rootAssemblyReference: "Playserv.Runtime.Modules.Server");
        }
    }
}
