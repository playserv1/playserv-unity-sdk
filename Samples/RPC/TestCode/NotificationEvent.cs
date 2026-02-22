using System;

namespace Playserv.Test.RPC
{
    public class NotificationEvent
    {
        public string EventId { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
    }
}
