using System;
using UnityEngine;

namespace Playserv.Spawn
{
    [Serializable]
    public sealed class SpawnEvent
    {
        public string SpawnId;
        public string AssetName;
        public Vector3 Position;
        public Quaternion Rotation;

        public SpawnEvent() { }

        public SpawnEvent(string assetName, Vector3 position, Quaternion rotation)
        {
            SpawnId = Guid.NewGuid().ToString();
            AssetName = assetName;
            Position = position;
            Rotation = rotation;
        }
    }
}
