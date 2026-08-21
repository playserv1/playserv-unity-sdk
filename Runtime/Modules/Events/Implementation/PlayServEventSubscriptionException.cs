using System;
using Playserv.Wrapper;

namespace Playserv.Events
{
    /// <summary>
    /// Reports a backend rejection of one event-type subscription without affecting other topics.
    /// </summary>
    public sealed class PlayServEventSubscriptionException : Exception
    {
        internal PlayServEventSubscriptionException(string eventType, int errorCode, string message)
            : base(string.IsNullOrWhiteSpace(message)
                ? $"The event subscription for '{eventType}' was rejected."
                : message)
        {
            EventType = eventType ?? string.Empty;
            UnifiedError = PlayServError.FromTransport(
                errorCode,
                errorCode > 0 ? $"event_{errorCode:D5}" : "event_subscription_rejected",
                Message,
                retryable: false);
        }

        /// <summary>The event type whose subscription was rejected.</summary>
        public string EventType { get; }

        /// <summary>Normalized error information.</summary>
        public PlayServError UnifiedError { get; }
    }
}
