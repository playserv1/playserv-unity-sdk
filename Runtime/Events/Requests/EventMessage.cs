using System;

namespace Playserv.Events.Requests
{
    /// <summary>
    /// Generic event message envelope transferred over transport.
    /// </summary>
    [Serializable]
    public sealed class EventMessage
    {
        /// <summary>
        /// Event payload type name.
        /// </summary>
        public string EventType;

        /// <summary>
        /// Serialized event payload JSON.
        /// </summary>
        public string Payload;
        
        /// <summary>
        /// Creates event message envelope.
        /// </summary>
        /// <param name="eventType">Event payload type name.</param>
        /// <param name="payload">Serialized event payload.</param>
        public EventMessage(string eventType, string payload)
        {
            this.EventType = eventType;
            this.Payload = payload;
        }
    }
}
