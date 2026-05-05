#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Playserv.Spawn
{
    public interface IPlayServSpawnModule
    {
        Task<GameObject> SpawnAsync(string assetName, Vector3 position, Quaternion rotation);

        string CurrentScope { get; }

        Task<bool> JoinScopeAsync(string groupName, CancellationToken ct = default);

        Task<bool> LeaveScopeAsync(CancellationToken ct = default);

        bool Despawn(string spawnId);

        bool Despawn(GameObject instance);

        void SetPrefabRegistry(INetworkPrefabRegistry prefabRegistry);

        bool TryGetSpawnedObject(string spawnId, out GameObject obj);

        NetworkObject GetNetworkObject(string networkId);
    }
}
#endif
