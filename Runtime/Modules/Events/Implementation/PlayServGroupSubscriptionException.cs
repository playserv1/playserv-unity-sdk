using System;
using Playserv.Wrapper;

namespace Playserv.Events
{
    /// <summary>Reports a backend rejection of a group subscription mutation.</summary>
    public sealed class PlayServGroupSubscriptionException : Exception
    {
        internal PlayServGroupSubscriptionException(
            string groupName,
            string operation,
            int errorCode,
            string message)
            : base(string.IsNullOrWhiteSpace(message)
                ? $"The group {operation} operation for '{groupName}' was rejected."
                : message)
        {
            GroupName = groupName ?? string.Empty;
            Operation = operation ?? string.Empty;
            UnifiedError = PlayServError.FromTransport(
                errorCode,
                SourceCodeFor(errorCode),
                Message,
                retryable: false,
                code: errorCode == (int)TransportErrorCode.GroupOutsideProject
                    ? PlayServErrorCode.Forbidden
                    : PlayServErrorCode.Transport);
        }

        /// <summary>The group whose subscription was mutated.</summary>
        public string GroupName { get; }

        /// <summary>The attempted operation, such as subscribe or unsubscribe.</summary>
        public string Operation { get; }

        /// <summary>Normalized backend failure.</summary>
        public PlayServError UnifiedError { get; }

        private static string SourceCodeFor(int errorCode) =>
            errorCode == (int)TransportErrorCode.GroupSubscriptionLimitReached
                ? "group_subscription_limit_reached"
                : errorCode == (int)TransportErrorCode.GroupOutsideProject
                ? "group_outside_project"
                : errorCode > 0
                    ? $"group_{errorCode:D5}"
                    : "group_subscription_rejected";
    }
}
