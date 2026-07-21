using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(40)]
    internal sealed class PlayServClientRpcModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.ClientRpcId,
                "RPC",
                "Client-side remote RPC commands, generated RPC helpers, and Invoke* wrapper APIs.",
                PlayServModuleManifest.DefineDisableClientRpc,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/RPC/Client/Implementation" },
                dependencyIds: new[] { PlayServModuleManifest.ClientExecutionId },
                hiddenDependencyAssetPaths: new[] { "Runtime/Modules/RPC/Core" },
                hiddenDependencyModuleIds: new[] { PlayServModuleManifest.RpcCoreId },
                rootAssemblyReference: "Playserv.Runtime.Modules.RPC.Client");
        }
    }
}
