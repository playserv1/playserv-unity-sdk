#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
namespace Playserv.Spawn
{
    internal readonly struct NetworkTransformSyncSettings
    {
        public NetworkTransformSyncSettings(
            bool syncPosition,
            bool syncRotation,
            bool syncScale,
            float positionThreshold,
            float rotationThreshold,
            float scaleThreshold,
            float renderDelay,
            float teleportDistance,
            float maxPredictionTime)
        {
            SyncPosition = syncPosition;
            SyncRotation = syncRotation;
            SyncScale = syncScale;
            PositionThreshold = positionThreshold;
            RotationThreshold = rotationThreshold;
            ScaleThreshold = scaleThreshold;
            RenderDelay = renderDelay;
            TeleportDistance = teleportDistance;
            MaxPredictionTime = maxPredictionTime;
        }

        public bool SyncPosition { get; }
        public bool SyncRotation { get; }
        public bool SyncScale { get; }
        public float PositionThreshold { get; }
        public float RotationThreshold { get; }
        public float ScaleThreshold { get; }
        public float RenderDelay { get; }
        public float TeleportDistance { get; }
        public float MaxPredictionTime { get; }
    }
}

#endif
