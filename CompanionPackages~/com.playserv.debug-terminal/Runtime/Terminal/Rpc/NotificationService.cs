using System;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed class NotificationService
    {
        public NotificationResult BroadcastToAll(string message)
        {
            if (string.IsNullOrEmpty(message))
                return NotificationResult.Fail("Message cannot be empty");

            PlayServEvents.Publish(new NotificationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Message = message,
                Timestamp = DateTime.UtcNow,
                EventType = "Broadcast"
            });

            return NotificationResult.Ok();
        }
    }

    public sealed class NotificationResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }

        public static NotificationResult Ok()
        {
            return new NotificationResult { Success = true };
        }

        public static NotificationResult Fail(string error)
        {
            return new NotificationResult { Error = error };
        }
    }
}
