#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Threading.Tasks;
using Playserv.Events;
using Playserv.Proxy.Logging;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;
using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class SpawnManager : IDisposable
    {
        private const int SpawnCompletionTimeoutMs = 10000;
        private static readonly ISdkLogger Logger = PlayServLog.ForCategory(PlayServLogCategory.Spawn);

        private readonly NetworkPrefabResolver _prefabResolver = new NetworkPrefabResolver();
        private readonly SpawnLifecycleStore _lifecycleStore = new SpawnLifecycleStore();
        private readonly SpawnEventPublisher _eventPublisher = new SpawnEventPublisher();
        private readonly LateJoinReplayService _lateJoinReplayService;

        private Func<string> _ownerIdProvider;
        private IDisposable _spawnSubscription;
        private IDisposable _despawnSubscription;
        private IDisposable _scopeJoinSubscription;

        public SpawnManager()
        {
            _lateJoinReplayService = new LateJoinReplayService(_lifecycleStore, _eventPublisher);
        }

        public void Initialize(IEventsAdapter eventsAdapter, Func<string> ownerIdProvider)
        {
            _spawnSubscription?.Dispose();
            _despawnSubscription?.Dispose();
            _scopeJoinSubscription?.Dispose();

            _ownerIdProvider = ownerIdProvider ?? (() => string.Empty);
            _eventPublisher.Initialize(eventsAdapter, ResolveOwnerId);
            _spawnSubscription = eventsAdapter.Subscribe<SpawnEvent>(OnSpawnEventReceived);
            _despawnSubscription = eventsAdapter.Subscribe<DespawnEvent>(OnDespawnEventReceived);
            _scopeJoinSubscription = eventsAdapter.Subscribe<SpawnScopeJoinedEvent>(OnSpawnScopeJoined);
        }

        public void SetPrefabRegistry(INetworkPrefabRegistry prefabRegistry)
        {
            _prefabResolver.SetRegistry(prefabRegistry);
        }

        public void SetDefaultScope(string groupName)
        {
            _eventPublisher.SetDefaultScope(groupName);
        }

        public void ClearDefaultScope()
        {
            _eventPublisher.ClearDefaultScope();
        }

        public Task<GameObject> SpawnAsync(string assetName, Vector3 position, Quaternion rotation)
        {
            if (!_prefabResolver.TryResolveNetworkPrefab(assetName, out var prefabId, out _))
                return Task.FromResult<GameObject>(null);

            var spawnEvent = new SpawnEvent(
                prefabId,
                position,
                rotation,
                ResolveOwnerId(),
                _lifecycleStore.NextLifecycleSeq());
            var tcs = new TaskCompletionSource<GameObject>(TaskCreationOptions.RunContinuationsAsynchronously);

            _lifecycleStore.AddPendingSpawn(spawnEvent.SpawnId, tcs);
            StartSpawnCompletionTimeout(spawnEvent.SpawnId, tcs);

            try
            {
                _eventPublisher.PublishSpawn(spawnEvent);
                ApplyLocalOptimisticSpawn(spawnEvent);
            }
            catch (Exception ex)
            {
                if (_lifecycleStore.TryRemovePendingSpawn(spawnEvent.SpawnId, out var pendingSpawn))
                    pendingSpawn.TrySetException(ex);
            }

            return tcs.Task;
        }

        public void PublishScopeJoined(string groupName)
        {
            _eventPublisher.PublishScopeJoined(groupName);
        }

        public bool Despawn(string spawnId)
        {
            if (string.IsNullOrWhiteSpace(spawnId))
                return false;

            var normalizedSpawnId = spawnId.Trim();
            var despawnEvent = new DespawnEvent(normalizedSpawnId, ResolveOwnerId(), _lifecycleStore.NextLifecycleSeq())
            {
                ScopeGroupName = _lifecycleStore.ResolveScopeForSpawn(
                    normalizedSpawnId,
                    _eventPublisher.DefaultScopeGroupName)
            };

            _eventPublisher.PublishDespawn(despawnEvent);
            return true;
        }

        public bool Despawn(GameObject instance)
        {
            if (instance == null)
                return false;

            var networkObject = instance.GetComponent<NetworkObject>();
            return networkObject != null && Despawn(networkObject.NetworkId);
        }

        public bool TryGetSpawnedObject(string spawnId, out GameObject obj)
        {
            return _lifecycleStore.TryGetSpawnedObject(spawnId, out obj);
        }

        public NetworkObject GetNetworkObject(string networkId)
        {
            return _lifecycleStore.GetNetworkObject(networkId);
        }

        private void ApplyLocalOptimisticSpawn(SpawnEvent spawnEvent)
        {
            OnSpawnEventReceived(spawnEvent);
        }

        private async void StartSpawnCompletionTimeout(string spawnId, TaskCompletionSource<GameObject> tcs)
        {
            try
            {
                await Task.Delay(SpawnCompletionTimeoutMs);

                if (tcs.Task.IsCompleted || !_lifecycleStore.TryRemovePendingSpawn(spawnId, out var pendingSpawn))
                    return;

                Logger.LogWarning($"Spawn timed out waiting for local completion: spawnId={spawnId}");
                pendingSpawn.TrySetResult(null);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Spawn timeout guard failed: {ex.Message}");
            }
        }

        private void OnSpawnEventReceived(SpawnEvent spawnEvent)
        {
            if (spawnEvent == null || string.IsNullOrWhiteSpace(spawnEvent.SpawnId))
                return;

            if (_lifecycleStore.TryGetSpawnedObject(spawnEvent.SpawnId, out var existingInstance))
            {
                _lifecycleStore.CompletePendingSpawn(spawnEvent.SpawnId, existingInstance);
                return;
            }

            if (_lifecycleStore.IsStaleLifecycleEvent(spawnEvent.SpawnId, spawnEvent.Seq))
                return;

            if (!_prefabResolver.TryResolveNetworkPrefab(spawnEvent.AssetName, out _, out var prefab))
            {
                _lifecycleStore.CompletePendingSpawn(spawnEvent.SpawnId, null);
                return;
            }

            var instance = UnityEngine.Object.Instantiate(prefab, spawnEvent.Position, spawnEvent.Rotation);
            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Logger.LogError($"Prefab '{spawnEvent.AssetName}' missing NetworkObject component");
                UnityEngine.Object.Destroy(instance);
                _lifecycleStore.CompletePendingSpawn(spawnEvent.SpawnId, null);
                return;
            }

            var ownerId = spawnEvent.OwnerId ?? string.Empty;
            var scopeGroupName = _eventPublisher.ResolveSpawnEventScope(spawnEvent);
            spawnEvent.ScopeGroupName = scopeGroupName;
            var isLocallyOwned = IsLocalOwner(ownerId) || _lifecycleStore.HasPendingSpawn(spawnEvent.SpawnId);

            networkObject.SetNetworkId(spawnEvent.SpawnId, ownerId, isLocallyOwned, scopeGroupName);
            _lifecycleStore.SetSpawnedObject(spawnEvent.SpawnId, instance);

            if (isLocallyOwned)
                _lifecycleStore.TrackOwnedSpawn(spawnEvent);

            _lifecycleStore.CompletePendingSpawn(spawnEvent.SpawnId, instance);
        }

        private void OnDespawnEventReceived(DespawnEvent despawnEvent)
        {
            if (despawnEvent == null || string.IsNullOrWhiteSpace(despawnEvent.SpawnId))
                return;

            if (!_lifecycleStore.TryGetSpawnedObject(despawnEvent.SpawnId, out var instance))
            {
                if (_lifecycleStore.IsStaleLifecycleEvent(despawnEvent.SpawnId, despawnEvent.Seq))
                    return;

                _lifecycleStore.CompletePendingSpawn(despawnEvent.SpawnId, null);
                return;
            }

            var networkObject = instance.GetComponent<NetworkObject>();
            if (IsForeignScopeDespawn(despawnEvent, networkObject))
                return;

            if (IsForeignOwnerDespawn(despawnEvent, networkObject))
            {
                Logger.LogWarning($"Ignoring despawn for '{despawnEvent.SpawnId}' from non-owner '{despawnEvent.OwnerId}'.");
                return;
            }

            if (_lifecycleStore.IsStaleLifecycleEvent(despawnEvent.SpawnId, despawnEvent.Seq))
                return;

            _lifecycleStore.RemoveSpawnedObject(despawnEvent.SpawnId, out _);
            _lifecycleStore.RemoveOwnedSpawn(despawnEvent.SpawnId);
            _lifecycleStore.CompletePendingSpawn(despawnEvent.SpawnId, null);
            UnityEngine.Object.Destroy(instance);
        }

        private void OnSpawnScopeJoined(SpawnScopeJoinedEvent joinedEvent)
        {
            if (joinedEvent == null)
                return;

            var localOwnerId = ResolveOwnerId();
            if (!string.IsNullOrEmpty(joinedEvent.OwnerId) &&
                string.Equals(joinedEvent.OwnerId, localOwnerId, StringComparison.Ordinal))
            {
                return;
            }

            _lateJoinReplayService.RepublishOwnedSpawnSnapshots(joinedEvent.GroupName);
        }

        private bool IsForeignScopeDespawn(DespawnEvent despawnEvent, NetworkObject networkObject)
        {
            var eventScopeGroupName = SpawnEventPublisher.NormalizeScopeGroupName(despawnEvent.ScopeGroupName);
            var objectScopeGroupName = networkObject == null
                ? null
                : SpawnEventPublisher.NormalizeScopeGroupName(networkObject.ScopeGroupName);

            return !string.IsNullOrEmpty(eventScopeGroupName) &&
                   !string.IsNullOrEmpty(objectScopeGroupName) &&
                   !string.Equals(eventScopeGroupName, objectScopeGroupName, StringComparison.Ordinal);
        }

        private static bool IsForeignOwnerDespawn(DespawnEvent despawnEvent, NetworkObject networkObject)
        {
            return networkObject != null &&
                   !string.IsNullOrEmpty(networkObject.OwnerId) &&
                   !string.IsNullOrEmpty(despawnEvent.OwnerId) &&
                   !string.Equals(networkObject.OwnerId, despawnEvent.OwnerId, StringComparison.Ordinal);
        }

        private string ResolveOwnerId()
        {
            return _ownerIdProvider?.Invoke() ?? string.Empty;
        }

        private bool IsLocalOwner(string ownerId)
        {
            return !string.IsNullOrEmpty(ownerId) &&
                   string.Equals(ownerId, ResolveOwnerId(), StringComparison.Ordinal);
        }

        public void Dispose()
        {
            _spawnSubscription?.Dispose();
            _despawnSubscription?.Dispose();
            _scopeJoinSubscription?.Dispose();
            _spawnSubscription = null;
            _despawnSubscription = null;
            _scopeJoinSubscription = null;

            _lifecycleStore.Clear();
            _eventPublisher.Clear();
            _ownerIdProvider = null;
        }
    }
}
#endif
