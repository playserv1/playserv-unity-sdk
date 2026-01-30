using System;

namespace Playserv.Events.Requests
{
    [Serializable]
    public sealed class EventMessage
    {
        public string EventType;
        public string Payload;
        
        public EventMessage(string eventType, string payload)
        {
            this.EventType = eventType;
            this.Payload = payload;
        }
    }
}
