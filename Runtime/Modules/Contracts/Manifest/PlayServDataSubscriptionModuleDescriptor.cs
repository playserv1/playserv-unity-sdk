using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(20)]
    internal sealed class PlayServDataSubscriptionModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.DataSubscriptionId,
                "Data Subscription",
                "Shared entity query, mutation, polling, and live subscription APIs.",
                PlayServModuleManifest.DefineDisableData,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/DataSubscription/Implementation" },
                dependencyIds: null,
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null,
                rootAssemblyReference: "Playserv.Runtime.Modules.DataSubscription");
        }
    }
}
