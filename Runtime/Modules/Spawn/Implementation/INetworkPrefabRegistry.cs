using UnityEngine;

namespace Playserv.Spawn
{
    public interface INetworkPrefabRegistry
    {
        bool TryLoadPrefab(string prefabId, out GameObject prefab);
    }
}
