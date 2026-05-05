#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System.Threading.Tasks;
using System.Threading;
using Playserv.Spawn;
using UnityEngine;

namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation
    {
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            GetModuleServices().Get<IPlayServSpawnModule>().SpawnAsync(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Spawn(assetName, position, Quaternion.identity);

        public string CurrentSpawnScope =>
            GetModuleServices().Get<IPlayServSpawnModule>().CurrentScope;

        public Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>
            GetModuleServices().Get<IPlayServSpawnModule>().JoinScopeAsync(groupName, ct);

        public Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>
            GetModuleServices().Get<IPlayServSpawnModule>().LeaveScopeAsync(ct);

        public bool Despawn(string spawnId) =>
            GetModuleServices().Get<IPlayServSpawnModule>().Despawn(spawnId);

        public bool Despawn(GameObject instance) =>
            GetModuleServices().Get<IPlayServSpawnModule>().Despawn(instance);

        public void SetSpawnPrefabRegistry(INetworkPrefabRegistry prefabRegistry) =>
            GetModuleServices().Get<IPlayServSpawnModule>().SetPrefabRegistry(prefabRegistry);
    }
}
#endif
