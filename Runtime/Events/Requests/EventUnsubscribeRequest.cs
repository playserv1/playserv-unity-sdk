namespace Playserv.Events.Requests
{
    [System.Serializable]
    public sealed class EventUnsubscribeRequest
    {
        public string EventSubscriptionId;

        public EventUnsubscribeRequest(string eventSubscriptionId)
        {
            EventSubscriptionId = eventSubscriptionId;
        }
    }
}




