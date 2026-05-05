#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class SnapshotInterpolator
    {
        private const int SnapshotBufferSize = 4;

        private readonly Transform _transform;
        private readonly TransformSnapshot[] _snapshotBuffer = new TransformSnapshot[SnapshotBufferSize];
        private int _snapshotCount;
        private bool _hasInitialized;

        public SnapshotInterpolator(Transform transform)
        {
            _transform = transform;
        }

        public bool HasTarget => _hasInitialized;

        public int SnapshotCount => _snapshotCount;

        public float TimeSinceLastSnapshot => _snapshotCount > 0
            ? Time.time - _snapshotBuffer[_snapshotCount - 1].RecvTime
            : 0f;

        public bool AddSnapshot(TransformSnapshot snapshot, bool teleport, NetworkTransformSyncSettings settings)
        {
            if (!_hasInitialized || teleport)
            {
                InitializeWithSnapshot(snapshot, settings);
                return false;
            }

            if (settings.SyncPosition && snapshot.HasPosition && _snapshotCount > 0)
            {
                var lastSnapshot = _snapshotBuffer[_snapshotCount - 1];
                if (lastSnapshot.HasPosition &&
                    Vector3.Distance(lastSnapshot.Position, snapshot.Position) > settings.TeleportDistance)
                {
                    InitializeWithSnapshot(snapshot, settings);
                    return true;
                }
            }

            AddSnapshotToBuffer(snapshot);
            return false;
        }

        public bool Update(NetworkTransformSyncSettings settings)
        {
            if (!_hasInitialized || _snapshotCount < 1)
                return false;

            var targetTime = Time.time - settings.RenderDelay;

            if (!FindSnapshotsForTime(targetTime, out var indexA, out var indexB))
            {
                ApplySnapshot(_snapshotBuffer[0], settings);
                return false;
            }

            if (indexA == indexB)
            {
                ApplyPredictedSnapshot(_snapshotBuffer[indexA], targetTime, settings);
                return true;
            }

            ApplyInterpolatedSnapshot(_snapshotBuffer[indexA], _snapshotBuffer[indexB], targetTime, settings);
            return false;
        }

        public void Reset()
        {
            _snapshotCount = 0;
            _hasInitialized = false;
        }

        private void InitializeWithSnapshot(TransformSnapshot snapshot, NetworkTransformSyncSettings settings)
        {
            for (var i = 0; i < SnapshotBufferSize; i++)
                _snapshotBuffer[i] = snapshot;

            _snapshotCount = SnapshotBufferSize;
            ApplySnapshot(snapshot, settings);
            _hasInitialized = true;
        }

        private void AddSnapshotToBuffer(TransformSnapshot snapshot)
        {
            if (_snapshotCount >= SnapshotBufferSize)
            {
                for (var i = 0; i < SnapshotBufferSize - 1; i++)
                    _snapshotBuffer[i] = _snapshotBuffer[i + 1];

                _snapshotCount = SnapshotBufferSize - 1;
            }

            _snapshotBuffer[_snapshotCount] = snapshot;
            _snapshotCount++;
        }

        private bool FindSnapshotsForTime(float targetTime, out int indexA, out int indexB)
        {
            indexA = -1;
            indexB = -1;

            for (var i = _snapshotCount - 1; i >= 0; i--)
            {
                if (_snapshotBuffer[i].RecvTime <= targetTime)
                {
                    indexA = i;
                    break;
                }
            }

            if (indexA < 0)
                return false;

            indexB = indexA < _snapshotCount - 1 ? indexA + 1 : indexA;
            return true;
        }

        private void ApplyInterpolatedSnapshot(
            TransformSnapshot snapshotA,
            TransformSnapshot snapshotB,
            float targetTime,
            NetworkTransformSyncSettings settings)
        {
            var timeDelta = snapshotB.RecvTime - snapshotA.RecvTime;
            var t = timeDelta > 0f
                ? Mathf.Clamp01((targetTime - snapshotA.RecvTime) / timeDelta)
                : 0f;

            if (settings.SyncPosition && snapshotA.HasPosition && snapshotB.HasPosition)
                _transform.position = Vector3.Lerp(snapshotA.Position, snapshotB.Position, t);

            if (settings.SyncRotation && snapshotA.HasRotation && snapshotB.HasRotation)
                _transform.rotation = Quaternion.Slerp(snapshotA.Rotation, snapshotB.Rotation, t);

            if (settings.SyncScale && snapshotA.HasScale && snapshotB.HasScale)
                _transform.localScale = Vector3.Lerp(snapshotA.Scale, snapshotB.Scale, t);
        }

        private void ApplySnapshot(TransformSnapshot snapshot, NetworkTransformSyncSettings settings)
        {
            if (settings.SyncPosition && snapshot.HasPosition)
                _transform.position = snapshot.Position;

            if (settings.SyncRotation && snapshot.HasRotation)
                _transform.rotation = snapshot.Rotation;

            if (settings.SyncScale && snapshot.HasScale)
                _transform.localScale = snapshot.Scale;
        }

        private void ApplyPredictedSnapshot(
            TransformSnapshot snapshot,
            float targetTime,
            NetworkTransformSyncSettings settings)
        {
            var predictionTime = Mathf.Clamp(targetTime - snapshot.RecvTime, 0f, settings.MaxPredictionTime);

            if (settings.SyncPosition && snapshot.HasPosition)
                _transform.position = snapshot.Position + snapshot.Velocity * predictionTime;

            if (settings.SyncRotation && snapshot.HasRotation)
                _transform.rotation = Quaternion.Euler(0f, snapshot.AngularVelocityYaw * predictionTime, 0f) * snapshot.Rotation;

            if (settings.SyncScale && snapshot.HasScale)
                _transform.localScale = snapshot.Scale + snapshot.ScaleVelocity * predictionTime;
        }
    }
}

#endif
