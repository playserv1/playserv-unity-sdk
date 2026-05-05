#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;
using UnityEngine;

namespace Playserv.Spawn
{
    /// <summary>
    /// Event payload used to spawn synchronized prefab instance.
    /// </summary>
    [Serializable]
    public sealed class SpawnEvent
    {
        /// <summary>
        /// Unique spawn identifier shared by all clients.
        /// </summary>
        public string SpawnId;

        /// <summary>
        /// User id of the client that owns this spawned object.
        /// </summary>
        public string OwnerId;

        /// <summary>
        /// Optional event group used for this object's spawn/transform lifecycle.
        /// </summary>
        public string ScopeGroupName;

        /// <summary>
        /// Monotonic lifecycle sequence assigned by the owner.
        /// </summary>
        public uint Seq;

        /// <summary>
        /// Prefab registry id. Default registry resolves it as a Resources path.
        /// </summary>
        public string AssetName;

        /// <summary>
        /// Spawn world position.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// Spawn world rotation.
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// Parameterless constructor for deserialization.
        /// </summary>
        public SpawnEvent() { }

        /// <summary>
        /// Creates spawn event with auto-generated spawn id.
        /// </summary>
        /// <param name="assetName">Prefab path in Resources.</param>
        /// <param name="position">World position.</param>
        /// <param name="rotation">World rotation.</param>
        public SpawnEvent(string assetName, Vector3 position, Quaternion rotation, string ownerId = null, uint seq = 0)
        {
            SpawnId = Guid.NewGuid().ToString();
            OwnerId = ownerId ?? string.Empty;
            ScopeGroupName = string.Empty;
            Seq = seq;
            AssetName = assetName;
            Position = position;
            Rotation = rotation;
        }
    }
}
#endif
