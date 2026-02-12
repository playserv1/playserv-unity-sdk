namespace Playserv.Events.Requests
{
    /// <summary>
    /// Request to subscribe current connection to specific event type.
    /// </summary>
    [System.Serializable]
    public sealed class EventSubscribeRequest
    {
        /// <summary>
        /// Event type name to subscribe.
        /// </summary>
        public string EventType;

        /// <summary>
        /// Creates subscribe request.
        /// </summary>
        /// <param name="eventType">Event type name.</param>
        public EventSubscribeRequest(string eventType)
        {
            EventType = eventType;
        }
    }
}
