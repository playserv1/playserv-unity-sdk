#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.Spawn
{
    [Serializable]
    internal sealed class SpawnScopeJoinedEvent
    {
        public string GroupName;

        public string OwnerId;

        public SpawnScopeJoinedEvent() { }

        public SpawnScopeJoinedEvent(string groupName, string ownerId)
        {
            GroupName = groupName ?? string.Empty;
            OwnerId = ownerId ?? string.Empty;
        }
    }
}
#endif
