#if !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.Events
{
    /// <summary>
    /// Optional base class for event payloads.
    /// This exists for backward compatibility with server integrations that model events as classes.
    /// </summary>
    public abstract class Event
    {
        /// <summary>
        /// Logical event type name.
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// Optional event identifier.
        /// </summary>
        public string EventId { get; set; } = string.Empty;

        /// <summary>
        /// Optional human-readable message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Event timestamp in UTC.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}

#endif
