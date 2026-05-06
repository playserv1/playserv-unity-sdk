using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class ResourcesNetworkPrefabRegistry : INetworkPrefabRegistry
    {
        public bool TryLoadPrefab(string prefabId, out GameObject prefab)
        {
            prefab = string.IsNullOrWhiteSpace(prefabId)
                ? null
                : Resources.Load<GameObject>(prefabId);

            return prefab != null;
        }
    }
}
