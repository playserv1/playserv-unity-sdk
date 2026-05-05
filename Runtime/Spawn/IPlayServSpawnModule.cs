#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System.Threading.Tasks;
using UnityEngine;

namespace Playserv.Spawn
{
    public interface IPlayServSpawnModule
    {
        Task<GameObject> SpawnAsync(string assetName, Vector3 position, Quaternion rotation);

        bool TryGetSpawnedObject(string spawnId, out GameObject obj);

        NetworkObject GetNetworkObject(string networkId);
    }
}
#endif
