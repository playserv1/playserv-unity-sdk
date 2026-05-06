using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Generic event message envelope transferred over transport.
    /// Kept in core because keepalive and event modules both use the same wire command.
    /// </summary>
    [Serializable]
    public sealed class EventMessage
    {
        public string EventType;
        public string Payload;

        public EventMessage(string eventType, string payload)
        {
            EventType = eventType;
            Payload = payload;
        }
    }
}
