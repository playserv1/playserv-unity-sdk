#if UNITY_5_3_OR_NEWER
using System;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Spawn
{
    /// <summary>
    /// Synchronizes transform changes for spawned network objects.
    /// Local owner publishes updates, remote clients interpolate received snapshots.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkTransform : MonoBehaviour
    {
        private const int SnapshotBufferSize = 4;

        [field: SerializeField]
        /// <summary>
        /// Enables position synchronization.
        /// </summary>
        public bool SyncPosition { get; set; } = true;

        [field: SerializeField]
        /// <summary>
        /// Enables rotation synchronization.
        /// </summary>
        public bool SyncRotation { get; set; } = true;

        [field: SerializeField]
        /// <summary>
        /// Enables scale synchronization.
        /// </summary>
        public bool SyncScale { get; set; }

        [Header("Send Thresholds")]
        [SerializeField] private float positionThreshold = 0.001f;
        [SerializeField] private float rotationThreshold = 0.1f;
        [SerializeField] private float scaleThreshold = 0.001f;

        [Header("Interpolation")]
        [SerializeField] private float renderDelay = 0.12f;
        [SerializeField] private float teleportDistance = 3f;

        private NetworkObject _networkObject;
        private IDisposable _subscription;
        private float _nextSyncTime;
        private float _syncInterval;
        private uint _sendSeq;

        // Owner state for velocity calculation
        private Vector3 _lastSentPosition;
        private Quaternion _lastSentRotation;
        private Vector3 _lastSentScale;
        private float _lastSendTime;

        // Snapshot buffer for interpolation
        private readonly TransformSnapshot[] _snapshotBuffer = new TransformSnapshot[SnapshotBufferSize];
        private int _snapshotCount;
        private bool _hasInitialized;

        // Debug stats
        private int _packetsReceived;
        private int _snapCount;
        private int _freezeCount;
        private float _lastDebugTime;

        /// <summary>
        /// Returns true after first remote snapshot was applied.
        /// </summary>
        public bool HasTarget => _hasInitialized;

        /// <summary>
        /// Time elapsed since last snapshot was received.
        /// </summary>
        public float TimeSinceLastSnapshot => _snapshotCount > 0
            ? Time.time - _snapshotBuffer[_snapshotCount - 1].RecvTime
            : 0f;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();

            var config = Resources.Load<PlayServConfig>("PlayServConfig");
            var syncIntervalMs = 50;
            if (config != null)
            {
#if UNITY_EDITOR
                var resolvedSettings = PlayServEnvironmentResolver.ResolveSettingsForEditor(
                    config,
                    out _,
                    out _,
                    out _);

                syncIntervalMs = resolvedSettings.NetworkTransformSyncIntervalMs;
#else
                syncIntervalMs = config.NetworkTransformSyncIntervalMs;
#endif
            }

            _syncInterval = syncIntervalMs / 1000f;

            InitializeLocalState();
        }

        private void InitializeLocalState()
        {
            _lastSentPosition = transform.position;
            _lastSentRotation = transform.rotation;
            _lastSentScale = transform.localScale;
            _lastSendTime = Time.time;
        }

        private void OnEnable()
        {
            _subscription = PlayServ.Subscribe<TransformSyncEvent>(OnTransformSyncReceived);
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Update()
        {
            if (_networkObject.IsLocallyOwned)
            {
                TrySendTransformUpdate();
            }
            else
            {
                UpdateRemoteTransform();
            }
        }

        private void TrySendTransformUpdate()
        {
            if (Time.time < _nextSyncTime)
                return;

            if (!HasTransformChanged())
                return;

            SendTransformUpdate();
            _nextSyncTime = Time.time + _syncInterval;
        }

        private bool HasTransformChanged()
        {
            if (SyncPosition && Vector3.Distance(transform.position, _lastSentPosition) > positionThreshold)
                return true;

            if (SyncRotation && Quaternion.Angle(transform.rotation, _lastSentRotation) > rotationThreshold)
                return true;

            if (SyncScale && Vector3.Distance(transform.localScale, _lastSentScale) > scaleThreshold)
                return true;

            return false;
        }

        private void SendTransformUpdate()
        {
            var networkId = _networkObject.NetworkId;
            if (string.IsNullOrEmpty(networkId))
                return;

            float currentTime = Time.time;
            float stepTime = currentTime - _lastSendTime;
            if (stepTime <= 0f)
                stepTime = _syncInterval;

            Vector3 velocity = Vector3.zero;
            float angVelYaw = 0f;
            Vector3 scaleVel = Vector3.zero;

            if (SyncPosition)
            {
                velocity = (transform.position - _lastSentPosition) / stepTime;
            }

            if (SyncRotation)
            {
                angVelYaw = CalculateYawVelocity(_lastSentRotation, transform.rotation, stepTime);
            }

            if (SyncScale)
            {
                scaleVel = (transform.localScale - _lastSentScale) / stepTime;
            }

            _lastSentPosition = transform.position;
            _lastSentRotation = transform.rotation;
            _lastSentScale = transform.localScale;
            _lastSendTime = currentTime;
            _sendSeq++;

            var syncEvent = new TransformSyncEvent(
                networkId,
                _sendSeq,
                SyncPosition ? _lastSentPosition : Vector3.zero,
                SyncRotation ? _lastSentRotation : Quaternion.identity,
                SyncScale ? _lastSentScale : Vector3.one,
                velocity,
                angVelYaw,
                scaleVel,
                stepTime);

            PlayServ.Publish(syncEvent);
        }

        private static float CalculateYawVelocity(Quaternion from, Quaternion to, float deltaTime)
        {
            float fromYaw = from.eulerAngles.y;
            float toYaw = to.eulerAngles.y;

            float deltaYaw = Mathf.DeltaAngle(fromYaw, toYaw);
            return deltaYaw / deltaTime;
        }

        private void OnTransformSyncReceived(TransformSyncEvent syncEvent)
        {
            if (syncEvent.NetworkId != _networkObject.NetworkId)
                return;

            if (_networkObject.IsLocallyOwned)
                return;

            _packetsReceived++;

            var snapshot = new TransformSnapshot
            {
                RecvTime = Time.time,
                Position = syncEvent.Position,
                Rotation = syncEvent.Rotation,
                Scale = syncEvent.Scale
            };

            if (!_hasInitialized || syncEvent.Teleport)
            {
                InitializeWithSnapshot(snapshot);
                return;
            }

            // Check for teleport (large distance)
            if (_snapshotCount > 0)
            {
                var lastSnapshot = _snapshotBuffer[_snapshotCount - 1];
                if (Vector3.Distance(lastSnapshot.Position, snapshot.Position) > teleportDistance)
                {
                    InitializeWithSnapshot(snapshot);
                    _snapCount++;
                    return;
                }
            }

            AddSnapshotToBuffer(snapshot);
        }

        private void InitializeWithSnapshot(TransformSnapshot snapshot)
        {
            // Fill buffer with identical snapshots to avoid interpolation issues
            for (int i = 0; i < SnapshotBufferSize; i++)
            {
                _snapshotBuffer[i] = snapshot;
            }
            _snapshotCount = SnapshotBufferSize;

            // Apply immediately
            if (SyncPosition)
                transform.position = snapshot.Position;
            if (SyncRotation)
                transform.rotation = snapshot.Rotation;
            if (SyncScale)
                transform.localScale = snapshot.Scale;

            _hasInitialized = true;
        }

        private void AddSnapshotToBuffer(TransformSnapshot snapshot)
        {
            // Shift buffer left if full
            if (_snapshotCount >= SnapshotBufferSize)
            {
                for (int i = 0; i < SnapshotBufferSize - 1; i++)
                {
                    _snapshotBuffer[i] = _snapshotBuffer[i + 1];
                }
                _snapshotCount = SnapshotBufferSize - 1;
            }

            _snapshotBuffer[_snapshotCount] = snapshot;
            _snapshotCount++;
        }

        private void UpdateRemoteTransform()
        {
            if (!_hasInitialized || _snapshotCount < 1)
                return;

            float targetTime = Time.time - renderDelay;

            if (!FindSnapshotsForTime(targetTime, out int indexA, out int indexB))
            {
                // targetTime is older than oldest snapshot - use oldest
                ApplySnapshot(_snapshotBuffer[0]);
                return;
            }

            if (indexA == indexB)
            {
                // targetTime is newer than newest snapshot - freeze at newest
                ApplySnapshot(_snapshotBuffer[indexA]);
                _freezeCount++;
                return;
            }

            // Interpolate between A and B
            var snapshotA = _snapshotBuffer[indexA];
            var snapshotB = _snapshotBuffer[indexB];

            float timeDelta = snapshotB.RecvTime - snapshotA.RecvTime;
            float t = timeDelta > 0f
                ? Mathf.Clamp01((targetTime - snapshotA.RecvTime) / timeDelta)
                : 0f;

            if (SyncPosition)
                transform.position = Vector3.Lerp(snapshotA.Position, snapshotB.Position, t);

            if (SyncRotation)
                transform.rotation = Quaternion.Slerp(snapshotA.Rotation, snapshotB.Rotation, t);

            if (SyncScale)
                transform.localScale = Vector3.Lerp(snapshotA.Scale, snapshotB.Scale, t);
        }

        private bool FindSnapshotsForTime(float targetTime, out int indexA, out int indexB)
        {
            indexA = -1;
            indexB = -1;

            // Find A: newest snapshot where recvTime <= targetTime
            for (int i = _snapshotCount - 1; i >= 0; i--)
            {
                if (_snapshotBuffer[i].RecvTime <= targetTime)
                {
                    indexA = i;
                    break;
                }
            }

            // targetTime is older than all snapshots
            if (indexA < 0)
                return false;

            // Find B: next snapshot after A (earliest where recvTime >= targetTime)
            if (indexA < _snapshotCount - 1)
            {
                indexB = indexA + 1;
            }
            else
            {
                // No snapshot after A - targetTime is newer than newest
                indexB = indexA;
            }

            return true;
        }

        private void ApplySnapshot(TransformSnapshot snapshot)
        {
            if (SyncPosition)
                transform.position = snapshot.Position;

            if (SyncRotation)
                transform.rotation = snapshot.Rotation;

            if (SyncScale)
                transform.localScale = snapshot.Scale;
        }

        /// <summary>
        /// Forcefully teleports locally owned object and sends teleport sync event.
        /// </summary>
        /// <param name="position">New world position.</param>
        /// <param name="rotation">New world rotation.</param>
        /// <param name="scale">New local scale.</param>
        public void ForceTeleport(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!_networkObject.IsLocallyOwned)
                return;

            transform.position = position;
            transform.rotation = rotation;
            transform.localScale = scale;

            _lastSentPosition = position;
            _lastSentRotation = rotation;
            _lastSentScale = scale;

            var networkId = _networkObject.NetworkId;
            if (string.IsNullOrEmpty(networkId))
                return;

            _sendSeq++;

            var syncEvent = new TransformSyncEvent(
                networkId,
                _sendSeq,
                position,
                rotation,
                scale,
                Vector3.zero,
                0f,
                Vector3.zero,
                _syncInterval,
                teleport: true);

            PlayServ.Publish(syncEvent);
        }

        /// <summary>
        /// Resets local sync state and interpolation buffers.
        /// </summary>
        public void Reset()
        {
            InitializeLocalState();

            _snapshotCount = 0;
            _hasInitialized = false;

            _sendSeq = 0;
            _packetsReceived = 0;
            _snapCount = 0;
            _freezeCount = 0;
        }

        /// <summary>
        /// Returns runtime debug counters and resets per-period stats.
        /// </summary>
        /// <returns>Current debug snapshot.</returns>
        public DebugInfo GetDebugInfo()
        {
            float now = Time.time;
            float elapsed = now - _lastDebugTime;

            var info = new DebugInfo
            {
                PacketsPerSecond = elapsed > 0 ? _packetsReceived / elapsed : 0,
                SnapshotCount = _snapshotCount,
                SnapCount = _snapCount,
                FreezeCount = _freezeCount,
                TimeSinceLastSnapshot = TimeSinceLastSnapshot,
                RenderDelay = renderDelay
            };

            _packetsReceived = 0;
            _snapCount = 0;
            _freezeCount = 0;
            _lastDebugTime = now;

            return info;
        }

        /// <summary>
        /// Runtime diagnostics for transform synchronization.
        /// </summary>
        public struct DebugInfo
        {
            /// <summary>
            /// Received packets per second for last measurement interval.
            /// </summary>
            public float PacketsPerSecond;

            /// <summary>
            /// Number of buffered snapshots.
            /// </summary>
            public int SnapshotCount;

            /// <summary>
            /// Number of teleport snaps in interval.
            /// </summary>
            public int SnapCount;

            /// <summary>
            /// Number of frames using latest snapshot without interpolation pair.
            /// </summary>
            public int FreezeCount;

            /// <summary>
            /// Time since last snapshot arrival.
            /// </summary>
            public float TimeSinceLastSnapshot;

            /// <summary>
            /// Configured interpolation delay.
            /// </summary>
            public float RenderDelay;
        }

        private struct TransformSnapshot
        {
            public float RecvTime;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }
    }
}
#endif
