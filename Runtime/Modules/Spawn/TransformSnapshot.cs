using UnityEngine;

namespace Playserv.Spawn
{
    internal struct TransformSnapshot
    {
        public float RecvTime;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public bool HasPosition;
        public bool HasRotation;
        public bool HasScale;
        public Vector3 Velocity;
        public float AngularVelocityYaw;
        public Vector3 ScaleVelocity;

        public static TransformSnapshot FromEvent(TransformSyncEvent syncEvent, float recvTime)
        {
            return new TransformSnapshot
            {
                RecvTime = recvTime,
                Position = syncEvent.Position,
                Rotation = syncEvent.Rotation,
                Scale = syncEvent.Scale,
                HasPosition = syncEvent.HasPosition,
                HasRotation = syncEvent.HasRotation,
                HasScale = syncEvent.HasScale,
                Velocity = syncEvent.Velocity,
                AngularVelocityYaw = syncEvent.AngularVelocityYaw,
                ScaleVelocity = syncEvent.ScaleVelocity
            };
        }
    }
}
