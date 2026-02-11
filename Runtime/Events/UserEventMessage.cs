using System;

namespace Playserv.Events
{
    [Serializable]
    public sealed class UserEventMessage
    {
        public string UserId;
        public string EventType;
        public string Payload;

        public UserEventMessage(string userId, string eventType, string payload)
        {
            UserId = userId;
            EventType = eventType;
            Payload = payload;
        }
    }
}


