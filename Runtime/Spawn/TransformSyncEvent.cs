#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using UnityEngine;

namespace Playserv.Spawn
{
    /// <summary>
    /// Event payload used by <see cref="NetworkTransform"/> to synchronize transform state.
    /// </summary>
    public sealed class TransformSyncEvent
    {
        /// <summary>
        /// Network object id.
        /// </summary>
        public string NetworkId;

        /// <summary>
        /// User id of the client that owns this transform stream.
        /// </summary>
        public string OwnerId;

        /// <summary>
        /// Optional event group used for this object's transform stream.
        /// </summary>
        public string ScopeGroupName;

        /// <summary>
        /// Monotonic sequence number.
        /// </summary>
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
        /// <summary>
        /// Delta time used to compute velocities on sender side.
        /// </summary>
        public float StepTime;

        // Flags
        /// <summary>
        /// True when update should be applied as teleport snap.
        /// </summary>
        public bool Teleport;

        /// <summary>
        /// Parameterless constructor for deserialization.
        /// </summary>
        public TransformSyncEvent() { }

        /// <summary>
        /// Creates transform synchronization event.
        /// </summary>
        /// <param name="networkId">Network object id.</param>
        /// <param name="seq">Sequence number.</param>
        /// <param name="position">Position.</param>
        /// <param name="rotation">Rotation.</param>
        /// <param name="scale">Scale.</param>
        /// <param name="velocity">Linear velocity estimate.</param>
        /// <param name="angularVelocityYaw">Yaw angular velocity in degrees/sec.</param>
        /// <param name="scaleVelocity">Scale velocity estimate.</param>
        /// <param name="stepTime">Delta time used by sender.</param>
        /// <param name="teleport">Whether receiver should snap immediately.</param>
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
