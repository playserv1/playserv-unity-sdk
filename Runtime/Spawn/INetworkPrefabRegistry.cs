#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using UnityEngine;

namespace Playserv.Spawn
{
    public interface INetworkPrefabRegistry
    {
        bool TryLoadPrefab(string prefabId, out GameObject prefab);
    }
}
#endif
