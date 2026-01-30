using System;
using UnityEngine;

namespace Playserv.Spawn
{
    [Serializable]
    public sealed class TransformSyncEvent
    {
        public string NetworkId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public Vector3 ScaleVelocity;
        public float Timestamp;
        public float DeltaTime;

        public TransformSyncEvent() { }

        public TransformSyncEvent(
            string networkId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Vector3 velocity = default,
            Vector3 angularVelocity = default,
            Vector3 scaleVelocity = default,
            float timestamp = 0f,
            float deltaTime = 0f)
        {
            NetworkId = networkId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            ScaleVelocity = scaleVelocity;
            Timestamp = timestamp;
            DeltaTime = deltaTime;
        }
    }
}
