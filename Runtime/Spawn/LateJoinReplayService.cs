#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.Spawn
{
    internal sealed class LateJoinReplayService
    {
        private readonly SpawnLifecycleStore _lifecycleStore;
        private readonly SpawnEventPublisher _eventPublisher;

        public LateJoinReplayService(SpawnLifecycleStore lifecycleStore, SpawnEventPublisher eventPublisher)
        {
            _lifecycleStore = lifecycleStore ?? throw new ArgumentNullException(nameof(lifecycleStore));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        }

        public void RepublishOwnedSpawnSnapshots(string groupName)
        {
            groupName = SpawnEventPublisher.NormalizeScopeGroupName(groupName);
            if (string.IsNullOrEmpty(groupName))
                return;

            var snapshots = _lifecycleStore.GetOwnedSpawnSnapshots();
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = BuildCurrentSpawnSnapshot(snapshots[i]);
                if (snapshot == null)
                    continue;

                if (!string.Equals(
                        SpawnEventPublisher.NormalizeScopeGroupName(snapshot.ScopeGroupName),
                        groupName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                _eventPublisher.PublishSpawnToGroup(groupName, snapshot);
                ForceTransformSnapshot(snapshot.SpawnId);
            }
        }

        private SpawnEvent BuildCurrentSpawnSnapshot(SpawnEvent source)
        {
            var snapshot = SpawnLifecycleStore.CloneSpawnEvent(source);
            if (!_lifecycleStore.TryGetSpawnedObject(source.SpawnId, out var instance) || instance == null)
                return snapshot;

            snapshot.Position = instance.transform.position;
            snapshot.Rotation = instance.transform.rotation;
            return snapshot;
        }

        private void ForceTransformSnapshot(string spawnId)
        {
            if (!_lifecycleStore.TryGetSpawnedObject(spawnId, out var instance) || instance == null)
                return;

            var networkTransform = instance.GetComponent<NetworkTransform>();
            networkTransform?.ForceSendSnapshot();
        }
    }
}
#endif
