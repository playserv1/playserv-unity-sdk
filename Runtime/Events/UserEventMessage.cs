#if !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.Events
{
    /// <summary>
    /// Transport message for user-scoped event payload.
    /// </summary>
    [Serializable]
    public sealed class UserEventMessage
    {
        /// <summary>
        /// Target user id.
        /// </summary>
        public string UserId;

        /// <summary>
        /// Event payload type name.
        /// </summary>
        public string EventType;

        /// <summary>
        /// Serialized JSON payload.
        /// </summary>
        public string Payload;

        /// <summary>
        /// Creates user event message envelope.
        /// </summary>
        /// <param name="userId">Target user id.</param>
        /// <param name="eventType">Event payload type name.</param>
        /// <param name="payload">Serialized payload JSON.</param>
        public UserEventMessage(string userId, string eventType, string payload)
        {
            UserId = userId;
            EventType = eventType;
            Payload = payload;
        }
    }
}


#endif
