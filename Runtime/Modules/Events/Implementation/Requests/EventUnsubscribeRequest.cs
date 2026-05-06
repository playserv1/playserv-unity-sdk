namespace Playserv.Events.Requests
{
    /// <summary>
    /// Request to unsubscribe from previously created event subscription.
    /// </summary>
    [System.Serializable]
    public sealed class EventUnsubscribeRequest
    {
        /// <summary>
        /// Server-provided event subscription identifier.
        /// </summary>
        public string EventSubscriptionId;

        /// <summary>
        /// Creates unsubscribe request.
        /// </summary>
        /// <param name="eventSubscriptionId">Subscription identifier returned by server.</param>
        public EventUnsubscribeRequest(string eventSubscriptionId)
        {
            EventSubscriptionId = eventSubscriptionId;
        }
    }
}
