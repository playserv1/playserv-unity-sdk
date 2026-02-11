namespace Playserv.Events.Requests
{
    [System.Serializable]
    public sealed class EventSubscribeRequest
    {
        public string EventType;

        public EventSubscribeRequest(string eventType)
        {
            EventType = eventType;
        }
    }
}