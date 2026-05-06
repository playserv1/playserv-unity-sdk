using System;

namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Indicates that referenced subscription id does not exist.
    /// </summary>
    public sealed class SubscriptionNotFoundException : DataSubscriptionException
    {
        /// <summary>
        /// Error code used by backend for subscription-not-found.
        /// </summary>
        public const int Code = 41001;

        /// <summary>
        /// Missing subscription id.
        /// </summary>
        public long SubscriptionId { get; }

        /// <summary>
        /// Creates subscription not found exception.
        /// </summary>
        /// <param name="subscriptionId">Missing subscription id.</param>
        /// <param name="message">Optional custom message.</param>
        public SubscriptionNotFoundException(long subscriptionId, string message = null)
            : base(Code, message ?? "Subscription ID not found - possible race condition")
        {
            SubscriptionId = subscriptionId;
        }
    }
}
