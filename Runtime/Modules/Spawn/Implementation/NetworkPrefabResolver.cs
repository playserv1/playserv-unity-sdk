using Playserv.Proxy.Logging;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;
using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class NetworkPrefabResolver
    {
        private static readonly ISdkLogger Logger = PlayServLog.ForCategory(PlayServLogCategory.Spawn);
        private INetworkPrefabRegistry _prefabRegistry = new ResourcesNetworkPrefabRegistry();

        public void SetRegistry(INetworkPrefabRegistry prefabRegistry)
        {
            _prefabRegistry = prefabRegistry ?? new ResourcesNetworkPrefabRegistry();
        }

        public bool TryResolveNetworkPrefab(string assetName, out string prefabId, out GameObject prefab)
        {
            prefabId = NormalizePrefabId(assetName);
            if (!_prefabRegistry.TryLoadPrefab(prefabId, out prefab))
            {
                Logger.LogError($"Network prefab not found: {prefabId}");
                return false;
            }

            if (prefab.GetComponent<NetworkObject>() != null)
                return true;

            Logger.LogError($"Prefab '{prefabId}' must have NetworkObject component");
            prefab = null;
            return false;
        }

        private static string NormalizePrefabId(string assetName)
        {
            return string.IsNullOrWhiteSpace(assetName) ? string.Empty : assetName.Trim();
        }
    }
}
