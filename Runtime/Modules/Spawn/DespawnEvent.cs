#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.Spawn
{
    [Serializable]
    public sealed class DespawnEvent
    {
        public string SpawnId;

        public string OwnerId;

        public string ScopeGroupName;

        public uint Seq;

        public DespawnEvent() { }

        public DespawnEvent(string spawnId, string ownerId, uint seq)
        {
            SpawnId = spawnId;
            OwnerId = ownerId;
            ScopeGroupName = string.Empty;
            Seq = seq;
        }
    }
}
#endif
