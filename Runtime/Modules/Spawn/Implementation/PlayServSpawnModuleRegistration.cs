using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Spawn;
using Playserv.Wrapper;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.SpawnId,
    typeof(Playserv.Spawn.PlayServSpawnModule),
    60)]
[assembly: PlayServLegacyApi(
    PlayServModuleManifest.SpawnId,
    typeof(IPlayServSpawnApi),
    typeof(Playserv.Wrapper.PlayServApiSpawnFacade))]

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
            PlayServLegacyApiRegistry.Register<IPlayServSpawnApi, PlayServApiSpawnFacade>(
                PlayServModuleManifest.SpawnId,
                () => new PlayServApiSpawnFacade());
        }
    }
}

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiSpawnFacade : IPlayServSpawnApi
    {
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            PlayServSpawn.Spawn(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            PlayServSpawn.Spawn(assetName, position);

        public string CurrentSpawnScope => PlayServSpawn.CurrentScope;

        public Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            PlayServSpawn.JoinSpawnScopeAsync(groupName, ct);

        public Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            PlayServSpawn.LeaveSpawnScopeAsync(ct);

        public bool Despawn(string spawnId) =>
            PlayServSpawn.Despawn(spawnId);

        public bool Despawn(GameObject instance) =>
            PlayServSpawn.Despawn(instance);

    }
}
