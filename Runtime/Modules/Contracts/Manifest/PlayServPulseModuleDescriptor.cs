using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(70)]
    internal sealed class PlayServPulseModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.PulseId,
                "Pulse",
                "Realtime config placeholder module and future feature flag surface.",
                PlayServModuleManifest.DefineDisablePulse,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Pulse" },
                dependencyIds: new[] { PlayServModuleManifest.ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null,
                rootAssemblyReference: "Playserv.Runtime.Modules.Pulse");
        }
    }
}
