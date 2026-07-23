using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Events;
using Playserv.Proxy.Logging;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;
using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class SpawnService : IDisposable
    {
        private const int SpawnCompletionTimeoutMs = 10000;
        private static readonly ISdkLogger Logger = PlayServLog.ForCategory(PlayServLogCategory.Spawn);

        private readonly NetworkPrefabResolver _prefabResolver = new NetworkPrefabResolver();
        private readonly SpawnLifecycleStore _lifecycleStore = new SpawnLifecycleStore();
        private readonly SpawnEventPublisher _eventPublisher = new SpawnEventPublisher();
        private readonly LateJoinReplayService _lateJoinReplayService;
        private readonly object _lifecycleGate = new object();
        private readonly object _backgroundTaskGate = new object();
        private readonly HashSet<Task> _backgroundTasks = new HashSet<Task>();
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly int _spawnCompletionTimeoutMs;
        private readonly Func<int, CancellationToken, Task> _delayAsync;

        private Func<string> _ownerIdProvider;
        private IDisposable _spawnSubscription;
        private IDisposable _despawnSubscription;
        private IDisposable _scopeJoinSubscription;
        private bool _disposed;

        public SpawnService()
            : this(
                SpawnCompletionTimeoutMs,
                (delayMs, cancellationToken) => Task.Delay(delayMs, cancellationToken))
        {
        }

        internal SpawnService(
            int spawnCompletionTimeoutMs,
            Func<int, CancellationToken, Task> delayAsync)
        {
            if (spawnCompletionTimeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(spawnCompletionTimeoutMs));

            _spawnCompletionTimeoutMs = spawnCompletionTimeoutMs;
            _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
            _lateJoinReplayService = new LateJoinReplayService(_lifecycleStore, _eventPublisher);
        }

        public void Initialize(IEventsAdapter eventsAdapter, Func<string> ownerIdProvider)
        {
            ThrowIfDisposed();

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
            ThrowIfDisposed();

            if (!_prefabResolver.TryResolveNetworkPrefab(assetName, out var prefabId, out _))
                return Task.FromResult<GameObject>(null);

            var spawnEvent = new SpawnEvent(
                prefabId,
                position,
                rotation,
                ResolveOwnerId(),
                _lifecycleStore.NextLifecycleSeq());
            var tcs = new TaskCompletionSource<GameObject>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lifecycleGate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SpawnService));

                _lifecycleStore.AddPendingSpawn(spawnEvent.SpawnId, tcs);
                TrackBackgroundTask(WaitForSpawnCompletionTimeoutAsync(
                    spawnEvent.SpawnId,
                    tcs,
                    _lifetimeCancellation.Token));
            }

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

        private async Task WaitForSpawnCompletionTimeoutAsync(
            string spawnId,
            TaskCompletionSource<GameObject> tcs,
            CancellationToken cancellationToken)
        {
            try
            {
                await _delayAsync(_spawnCompletionTimeoutMs, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                lock (_lifecycleGate)
                {
                    if (_disposed ||
                        cancellationToken.IsCancellationRequested ||
                        tcs.Task.IsCompleted ||
                        !_lifecycleStore.TryRemovePendingSpawn(spawnId, out var pendingSpawn))
                    {
                        return;
                    }

                    Logger.LogWarning($"Spawn timed out waiting for local completion: spawnId={spawnId}");
                    pendingSpawn.TrySetResult(null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Spawn timeout guard failed: {ex.Message}");
            }
        }

        internal int BackgroundTaskCount
        {
            get
            {
                lock (_backgroundTaskGate)
                    return _backgroundTasks.Count;
            }
        }

        internal Task WaitForBackgroundTasksAsync()
        {
            lock (_backgroundTaskGate)
            {
                return _backgroundTasks.Count == 0
                    ? Task.CompletedTask
                    : Task.WhenAll(new List<Task>(_backgroundTasks));
            }
        }

        private void TrackBackgroundTask(Task task)
        {
            if (task == null)
                return;

            lock (_backgroundTaskGate)
                _backgroundTasks.Add(task);

            _ = task.ContinueWith(
                completedTask =>
                {
                    _ = completedTask.Exception;
                    lock (_backgroundTaskGate)
                        _backgroundTasks.Remove(completedTask);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void ThrowIfDisposed()
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SpawnService));
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
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _lifetimeCancellation.Cancel();
            }

            _spawnSubscription?.Dispose();
            _despawnSubscription?.Dispose();
            _scopeJoinSubscription?.Dispose();
            _spawnSubscription = null;
            _despawnSubscription = null;
            _scopeJoinSubscription = null;

            _lifecycleStore.Clear();
            _eventPublisher.Clear();
            _ownerIdProvider = null;
            _lifetimeCancellation.Dispose();
        }
    }
}
