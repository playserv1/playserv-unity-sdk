#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
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
#endif
