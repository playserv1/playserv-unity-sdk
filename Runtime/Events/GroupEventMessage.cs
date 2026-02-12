using System;

namespace Playserv.Events
{
    /// <summary>
    /// Transport message for group-scoped event payload.
    /// </summary>
    [Serializable]
    public sealed class GroupEventMessage
    {
        /// <summary>
        /// Target group name.
        /// </summary>
        public string GroupName;

        /// <summary>
        /// Event payload type name.
        /// </summary>
        public string EventType;

        /// <summary>
        /// Serialized JSON payload.
        /// </summary>
        public string Payload;

        /// <summary>
        /// Creates group event message envelope.
        /// </summary>
        /// <param name="groupName">Target group name.</param>
        /// <param name="eventType">Event payload type name.</param>
        /// <param name="payload">Serialized payload JSON.</param>
        public GroupEventMessage(string groupName, string eventType, string payload)
        {
            GroupName = groupName;
            EventType = eventType;
            Payload = payload;
        }
    }
}

