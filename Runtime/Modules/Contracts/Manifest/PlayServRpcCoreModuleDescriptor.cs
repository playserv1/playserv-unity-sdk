using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(30)]
    internal sealed class PlayServRpcCoreModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.RpcCoreId,
                "RPC Core",
                "Shared RPC attributes, payload mapping, and codec helpers.",
                PlayServModuleManifest.DefineDisableRpcCore,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: false,
                visibleInExport: false,
                assetPaths: new[] { "Runtime/Modules/RPC/Core" },
                dependencyIds: null,
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null,
                rootAssemblyReference: "Playserv.Runtime.Modules.RPC.Core");
        }
    }
}
