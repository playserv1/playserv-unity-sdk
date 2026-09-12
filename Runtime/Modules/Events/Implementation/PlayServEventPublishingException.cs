using System;
using Playserv.Wrapper;

namespace Playserv.Events
{
    /// <summary>
    /// Raised when game code attempts to publish an event over the player WebSocket.
    /// The current backend supports event subscriptions but has no runtime publish handler.
    /// </summary>
    public sealed class PlayServEventPublishingException : NotSupportedException
    {
        private PlayServEventPublishingException(string target, string message)
            : base(message)
        {
            Target = target ?? string.Empty;
            UnifiedError = new PlayServError(
                PlayServErrorCode.InvalidConfiguration,
                "event_publishing_not_supported",
                message,
                retryable: false);
        }

        public string Target { get; }

        public PlayServError UnifiedError { get; }

        internal static PlayServEventPublishingException Broadcast() =>
            new PlayServEventPublishingException(
                "broadcast",
                "Runtime event publishing is not supported by the current backend protocol. Use RPC, Records or Code for client-to-server mutations.");

        internal static PlayServEventPublishingException Group(string groupName) =>
            new PlayServEventPublishingException(
                "group:" + groupName,
                "Runtime group-event publishing is not supported by the current backend protocol.");

        internal static PlayServEventPublishingException User(string userId) =>
            new PlayServEventPublishingException(
                "user:" + userId,
                "Runtime user-event publishing is not supported by the current backend protocol.");
    }
}
