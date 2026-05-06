namespace Playserv.Events.Responses
{
    /// <summary>
    /// Event delivery envelope used by server for user/group-routed events.
    /// </summary>
    [System.Serializable]
    public sealed class EventSubscribedMessage
    {
        /// <summary>
        /// Server-side event subscription identifier.
        /// Used to resolve original subscribed event type on client.
        /// </summary>
        public string EventSubscriptionId;

        /// <summary>
        /// Serialized JSON payload of delivered event.
        /// </summary>
        public string Payload;
    }
}
