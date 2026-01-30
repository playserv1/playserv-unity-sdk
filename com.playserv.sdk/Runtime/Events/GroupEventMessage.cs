using System;

namespace Playserv.Events
{
    [Serializable]
    public sealed class GroupEventMessage
    {
        public string GroupName;
        public string EventType;
        public string Payload;

        public GroupEventMessage(string groupName, string eventType, string payload)
        {
            GroupName = groupName;
            EventType = eventType;
            Payload = payload;
        }
    }
}


