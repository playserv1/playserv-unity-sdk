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

        void SetTransformSyncIntervalMs(int intervalMs);

        bool TryGetSpawnedObject(string spawnId, out GameObject obj);

        NetworkObject GetNetworkObject(string networkId);
    }
}
