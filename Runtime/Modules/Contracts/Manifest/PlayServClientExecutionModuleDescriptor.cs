using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(0)]
    internal sealed class PlayServClientExecutionModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.ClientExecutionId,
                "Client Execution",
                "Client-side runtime execution surface. Disable this for server-only SDK builds.",
                PlayServModuleManifest.DefineDisableClientExecution,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: null,
                dependencyIds: null,
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null);
        }
    }
}
