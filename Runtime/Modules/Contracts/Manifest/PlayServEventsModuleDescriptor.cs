using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(10)]
    internal sealed class PlayServEventsModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.EventsId,
                "Events",
                "Typed publish/subscribe runtime and typed event API generation controls. Cannot be disabled while dependent modules are enabled.",
                PlayServModuleManifest.DefineDisableEvents,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Events/Implementation", "Editor/Events" },
                dependencyIds: new[] { PlayServModuleManifest.ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null,
                rootAssemblyReference: "Playserv.Runtime.Modules.Events");
        }
    }
}
