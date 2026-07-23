using Playserv.Modules;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.SpawnId,
    typeof(Playserv.Spawn.PlayServSpawnModule),
    60)]

namespace Playserv.Spawn
{
    internal static class PlayServSpawnModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServSpawnModule>(
                PlayServModuleManifest.SpawnId,
                60);
        }
    }
}
