using System;

namespace Playserv.DataSubscription.Exceptions
{
    public sealed class SubscriptionNotFoundException : DataSubscriptionException
    {
        public const int Code = 41001;

        public long SubscriptionId { get; }

        public SubscriptionNotFoundException(long subscriptionId, string message = null)
            : base(Code, message ?? "Subscription ID not found - possible race condition")
        {
            SubscriptionId = subscriptionId;
        }
    }
}
