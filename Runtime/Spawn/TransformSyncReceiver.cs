#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;
using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class TransformSyncReceiver
    {
        private readonly NetworkObject _networkObject;
        private readonly SnapshotInterpolator _interpolator;
        private uint _lastReceivedSeq;
        private int _packetsReceived;
        private int _snapCount;
        private int _freezeCount;
        private float _lastDebugTime;

        public TransformSyncReceiver(NetworkObject networkObject, Transform transform)
        {
            _networkObject = networkObject;
            _interpolator = new SnapshotInterpolator(transform);
        }

        public bool HasTarget => _interpolator.HasTarget;

        public float TimeSinceLastSnapshot => _interpolator.TimeSinceLastSnapshot;

        public void Receive(TransformSyncEvent syncEvent, NetworkTransformSyncSettings settings)
        {
            if (syncEvent == null || !ShouldAccept(syncEvent))
                return;

            _lastReceivedSeq = syncEvent.Seq;
            _packetsReceived++;

            var snapshot = TransformSnapshot.FromEvent(syncEvent, Time.time);
            if (_interpolator.AddSnapshot(snapshot, syncEvent.Teleport, settings))
                _snapCount++;
        }

        public void Update(NetworkTransformSyncSettings settings)
        {
            if (_interpolator.Update(settings))
                _freezeCount++;
        }

        public void Reset()
        {
            _interpolator.Reset();
            _lastReceivedSeq = 0;
            _packetsReceived = 0;
            _snapCount = 0;
            _freezeCount = 0;
            _lastDebugTime = Time.time;
        }

        public TransformSyncDiagnostics GetDiagnostics(float renderDelay)
        {
            var now = Time.time;
            var elapsed = now - _lastDebugTime;

            var diagnostics = new TransformSyncDiagnostics(
                elapsed > 0f ? _packetsReceived / elapsed : 0f,
                _interpolator.SnapshotCount,
                _snapCount,
                _freezeCount,
                TimeSinceLastSnapshot,
                renderDelay);

            _packetsReceived = 0;
            _snapCount = 0;
            _freezeCount = 0;
            _lastDebugTime = now;

            return diagnostics;
        }

        private bool ShouldAccept(TransformSyncEvent syncEvent)
        {
            if (!string.Equals(syncEvent.NetworkId, _networkObject.NetworkId, StringComparison.Ordinal))
                return false;

            if (_networkObject.IsLocallyOwned)
                return false;

            var objectScopeGroupName = SpawnEventPublisher.NormalizeScopeGroupName(_networkObject.ScopeGroupName);
            var eventScopeGroupName = SpawnEventPublisher.NormalizeScopeGroupName(syncEvent.ScopeGroupName);
            if (!string.IsNullOrEmpty(objectScopeGroupName) &&
                !string.IsNullOrEmpty(eventScopeGroupName) &&
                !string.Equals(objectScopeGroupName, eventScopeGroupName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(syncEvent.OwnerId) &&
                !string.IsNullOrEmpty(_networkObject.OwnerId) &&
                !string.Equals(syncEvent.OwnerId, _networkObject.OwnerId, StringComparison.Ordinal))
            {
                return false;
            }

            return !IsStaleSequence(syncEvent.Seq);
        }

        private bool IsStaleSequence(uint seq)
        {
            return seq != 0 && _lastReceivedSeq != 0 && seq <= _lastReceivedSeq;
        }
    }

    internal readonly struct TransformSyncDiagnostics
    {
        public TransformSyncDiagnostics(
            float packetsPerSecond,
            int snapshotCount,
            int snapCount,
            int freezeCount,
            float timeSinceLastSnapshot,
            float renderDelay)
        {
            PacketsPerSecond = packetsPerSecond;
            SnapshotCount = snapshotCount;
            SnapCount = snapCount;
            FreezeCount = freezeCount;
            TimeSinceLastSnapshot = timeSinceLastSnapshot;
            RenderDelay = renderDelay;
        }

        public float PacketsPerSecond { get; }
        public int SnapshotCount { get; }
        public int SnapCount { get; }
        public int FreezeCount { get; }
        public float TimeSinceLastSnapshot { get; }
        public float RenderDelay { get; }
    }
}

#endif
