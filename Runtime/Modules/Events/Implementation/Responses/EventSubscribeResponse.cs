namespace Playserv.Events.Responses
{
    /// <summary>
    /// Backend acknowledgement for an event-type subscription.
    /// </summary>
    [System.Serializable]
    public sealed class EventSubscribeResponse
    {
        /// <summary>
        /// <summary>Canonical event type used to correlate concurrent responses.</summary>
        public string EventType;

        /// <summary>Whether the subscription was accepted.</summary>
        public bool success;

        /// <summary>Backend numeric error code when <see cref="success"/> is false.</summary>
        public int errorCode;

        /// <summary>Backend error message when <see cref="success"/> is false.</summary>
        public string errorMessage;
    }
}
