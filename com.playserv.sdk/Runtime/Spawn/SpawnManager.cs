using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class SpawnManager : IDisposable
    {
        private readonly Dictionary<string, GameObject> _spawnedObjects = new();
        private readonly Dictionary<string, TaskCompletionSource<GameObject>> _pendingSpawns = new();
        private Action<SpawnEvent> _publishAction;
        private IDisposable _subscription;

        public void Initialize(Action<SpawnEvent> publishAction, Func<Action<SpawnEvent>, IDisposable> subscribeAction)
        {
            _subscription?.Dispose();
            _publishAction = publishAction;
            _subscription = subscribeAction(OnSpawnEventReceived);
        }

        public Task<GameObject> SpawnAsync(string assetName, Vector3 position, Quaternion rotation)
        {
            var prefab = Resources.Load<GameObject>(assetName);
            if (prefab == null)
            {
                Debug.LogError($"[SpawnManager] Prefab not found in Resources: {assetName}");
                return Task.FromResult<GameObject>(null);
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[SpawnManager] Prefab '{assetName}' must have NetworkObject component");
                return Task.FromResult<GameObject>(null);
            }

            var spawnEvent = new SpawnEvent(assetName, position, rotation);
            var tcs = new TaskCompletionSource<GameObject>();

            _pendingSpawns[spawnEvent.SpawnId] = tcs;
            _publishAction(spawnEvent);

            return tcs.Task;
        }

        private void OnSpawnEventReceived(SpawnEvent spawnEvent)
        {
            if (_spawnedObjects.ContainsKey(spawnEvent.SpawnId))
                return;

            var prefab = Resources.Load<GameObject>(spawnEvent.AssetName);
            if (prefab == null)
            {
                Debug.LogError($"[SpawnManager] Prefab not found in Resources: {spawnEvent.AssetName}");
                CompletePendingSpawn(spawnEvent.SpawnId, null);
                return;
            }

            var instance = UnityEngine.Object.Instantiate(prefab, spawnEvent.Position, spawnEvent.Rotation);

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError($"[SpawnManager] Prefab '{spawnEvent.AssetName}' missing NetworkObject component");
                UnityEngine.Object.Destroy(instance);
                CompletePendingSpawn(spawnEvent.SpawnId, null);
                return;
            }

            var isLocallyOwned = _pendingSpawns.ContainsKey(spawnEvent.SpawnId);
            networkObject.SetNetworkId(spawnEvent.SpawnId, isLocallyOwned);
            _spawnedObjects[spawnEvent.SpawnId] = instance;

            CompletePendingSpawn(spawnEvent.SpawnId, instance);
        }

        private void CompletePendingSpawn(string spawnId, GameObject instance)
        {
            if (!_pendingSpawns.TryGetValue(spawnId, out var tcs))
                return;

            _pendingSpawns.Remove(spawnId);
            tcs.TrySetResult(instance);
        }

        public bool TryGetSpawnedObject(string spawnId, out GameObject obj) =>
            _spawnedObjects.TryGetValue(spawnId, out obj);

        public NetworkObject GetNetworkObject(string networkId) =>
            TryGetSpawnedObject(networkId, out var obj) ? obj.GetComponent<NetworkObject>() : null;

        public void Dispose()
        {
            _subscription?.Dispose();
            _spawnedObjects.Clear();

            foreach (var tcs in _pendingSpawns.Values)
                tcs.TrySetCanceled();

            _pendingSpawns.Clear();
        }
    }
}
