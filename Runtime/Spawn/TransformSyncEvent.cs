#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Playserv.Spawn
{
    public sealed class TransformSyncEvent
    {
        public string NetworkId;
        public uint Seq;

        // Transform state
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        // Velocities for prediction
        public Vector3 Velocity;
        public float AngularVelocityYaw; // degrees/sec around Y axis
        public Vector3 ScaleVelocity;

        // Timing
        public float StepTime;

        // Flags
        public bool Teleport;

        public TransformSyncEvent() { }

        public TransformSyncEvent(
            string networkId,
            uint seq,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Vector3 velocity,
            float angularVelocityYaw,
            Vector3 scaleVelocity,
            float stepTime,
            bool teleport = false)
        {
            NetworkId = networkId;
            Seq = seq;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Velocity = velocity;
            AngularVelocityYaw = angularVelocityYaw;
            ScaleVelocity = scaleVelocity;
            StepTime = stepTime;
            Teleport = teleport;
        }
    }
}
#endif
