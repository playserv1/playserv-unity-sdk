#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class SpawnLifecycleStore
    {
        private readonly object _pendingSpawnGate = new();
        private readonly Dictionary<string, GameObject> _spawnedObjects = new();
        private readonly Dictionary<string, TaskCompletionSource<GameObject>> _pendingSpawns = new();
        private readonly Dictionary<string, uint> _lastLifecycleSeqBySpawnId = new();
        private readonly Dictionary<string, SpawnEvent> _ownedSpawnEvents = new();
        private uint _lifecycleSeq;

        public uint NextLifecycleSeq()
        {
            _lifecycleSeq++;
            return _lifecycleSeq == 0 ? ++_lifecycleSeq : _lifecycleSeq;
        }

        public bool IsStaleLifecycleEvent(string spawnId, uint seq)
        {
            if (seq == 0)
                return false;

            if (_lastLifecycleSeqBySpawnId.TryGetValue(spawnId, out var lastSeq) && seq <= lastSeq)
                return true;

            _lastLifecycleSeqBySpawnId[spawnId] = seq;
            return false;
        }

        public void AddPendingSpawn(string spawnId, TaskCompletionSource<GameObject> tcs)
        {
            lock (_pendingSpawnGate)
            {
                _pendingSpawns[spawnId] = tcs;
            }
        }

        public bool HasPendingSpawn(string spawnId)
        {
            lock (_pendingSpawnGate)
            {
                return _pendingSpawns.ContainsKey(spawnId);
            }
        }

        public bool TryRemovePendingSpawn(string spawnId, out TaskCompletionSource<GameObject> tcs)
        {
            lock (_pendingSpawnGate)
            {
                if (!_pendingSpawns.TryGetValue(spawnId, out tcs))
                    return false;

                _pendingSpawns.Remove(spawnId);
                return true;
            }
        }

        public bool CompletePendingSpawn(string spawnId, GameObject instance)
        {
            if (!TryRemovePendingSpawn(spawnId, out var tcs))
                return false;

            tcs.TrySetResult(instance);
            return true;
        }

        public void SetSpawnedObject(string spawnId, GameObject instance)
        {
            _spawnedObjects[spawnId] = instance;
        }

        public bool TryGetSpawnedObject(string spawnId, out GameObject obj)
        {
            return _spawnedObjects.TryGetValue(spawnId, out obj);
        }

        public NetworkObject GetNetworkObject(string networkId)
        {
            return TryGetSpawnedObject(networkId, out var obj) ? obj.GetComponent<NetworkObject>() : null;
        }

        public bool RemoveSpawnedObject(string spawnId, out GameObject instance)
        {
            if (!_spawnedObjects.TryGetValue(spawnId, out instance))
                return false;

            _spawnedObjects.Remove(spawnId);
            return true;
        }

        public void TrackOwnedSpawn(SpawnEvent spawnEvent)
        {
            _ownedSpawnEvents[spawnEvent.SpawnId] = CloneSpawnEvent(spawnEvent);
        }

        public void RemoveOwnedSpawn(string spawnId)
        {
            _ownedSpawnEvents.Remove(spawnId);
        }

        public List<SpawnEvent> GetOwnedSpawnSnapshots()
        {
            return new List<SpawnEvent>(_ownedSpawnEvents.Values);
        }

        public string ResolveScopeForSpawn(string spawnId, string defaultScopeGroupName)
        {
            if (TryGetSpawnedObject(spawnId, out var instance) && instance != null)
            {
                var networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject != null && !string.IsNullOrEmpty(networkObject.ScopeGroupName))
                    return networkObject.ScopeGroupName;
            }

            return defaultScopeGroupName ?? string.Empty;
        }

        public void Clear()
        {
            _spawnedObjects.Clear();
            _lastLifecycleSeqBySpawnId.Clear();
            _ownedSpawnEvents.Clear();

            var pendingSpawns = new List<TaskCompletionSource<GameObject>>();
            lock (_pendingSpawnGate)
            {
                foreach (var tcs in _pendingSpawns.Values)
                    pendingSpawns.Add(tcs);

                _pendingSpawns.Clear();
            }

            foreach (var tcs in pendingSpawns)
                tcs.TrySetCanceled();
        }

        public static SpawnEvent CloneSpawnEvent(SpawnEvent source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return new SpawnEvent
            {
                SpawnId = source.SpawnId,
                OwnerId = source.OwnerId,
                ScopeGroupName = source.ScopeGroupName,
                Seq = source.Seq,
                AssetName = source.AssetName,
                Position = source.Position,
                Rotation = source.Rotation
            };
        }
    }
}
#endif
