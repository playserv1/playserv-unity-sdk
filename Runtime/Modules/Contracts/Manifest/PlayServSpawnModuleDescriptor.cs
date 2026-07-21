using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(60)]
    internal sealed class PlayServSpawnModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.SpawnId,
                "Spawn",
                "NetworkObject/NetworkTransform helpers and Resources-based spawn facade. Depends on Events.",
                PlayServModuleManifest.DefineDisableSpawn,
                defaultEnabled: true,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/Spawn/Implementation" },
                dependencyIds: new[] { PlayServModuleManifest.EventsId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null,
                rootAssemblyReference: "Playserv.Runtime.Modules.Spawn");
        }
    }
}
