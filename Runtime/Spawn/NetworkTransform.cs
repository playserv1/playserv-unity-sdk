#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
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
        [SerializeField] private float maxPredictionTime = 0.25f;

        private NetworkObject _networkObject;
        private TransformSyncSender _sender;
        private TransformSyncReceiver _receiver;
        private IDisposable _subscription;

        /// <summary>
        /// Returns true after first remote snapshot was applied.
        /// </summary>
        public bool HasTarget => _receiver != null && _receiver.HasTarget;

        /// <summary>
        /// Time elapsed since last snapshot was received.
        /// </summary>
        public float TimeSinceLastSnapshot => _receiver?.TimeSinceLastSnapshot ?? 0f;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();

            var syncInterval = NetworkTransformSettingsProvider.ResolveSyncIntervalSeconds();
            _sender = new TransformSyncSender(_networkObject, transform, syncInterval);
            _receiver = new TransformSyncReceiver(_networkObject, transform);
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
            var settings = BuildSettings();
            if (_networkObject.IsLocallyOwned)
            {
                _sender.TrySend(settings);
                return;
            }

            _receiver.Update(settings);
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

            _sender.ResetLocalState();
            _sender.SendSnapshot(BuildSettings(), teleport: true, forceZeroVelocity: true);
        }

        internal void ForceSendSnapshot()
        {
            if (!_networkObject.IsLocallyOwned)
                return;

            _sender.SendSnapshot(BuildSettings(), teleport: true, forceZeroVelocity: true);
        }

        /// <summary>
        /// Resets local sync state and interpolation buffers.
        /// </summary>
        public void Reset()
        {
            _sender.Reset();
            _receiver.Reset();
        }

        /// <summary>
        /// Returns runtime debug counters and resets per-period stats.
        /// </summary>
        /// <returns>Current debug snapshot.</returns>
        public DebugInfo GetDebugInfo()
        {
            var diagnostics = _receiver.GetDiagnostics(renderDelay);
            return new DebugInfo
            {
                PacketsPerSecond = diagnostics.PacketsPerSecond,
                SnapshotCount = diagnostics.SnapshotCount,
                SnapCount = diagnostics.SnapCount,
                FreezeCount = diagnostics.FreezeCount,
                TimeSinceLastSnapshot = diagnostics.TimeSinceLastSnapshot,
                RenderDelay = diagnostics.RenderDelay
            };
        }

        private void OnTransformSyncReceived(TransformSyncEvent syncEvent)
        {
            _receiver.Receive(syncEvent, BuildSettings());
        }

        private NetworkTransformSyncSettings BuildSettings()
        {
            return new NetworkTransformSyncSettings(
                SyncPosition,
                SyncRotation,
                SyncScale,
                positionThreshold,
                rotationThreshold,
                scaleThreshold,
                renderDelay,
                teleportDistance,
                maxPredictionTime);
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
    }
}

#endif
