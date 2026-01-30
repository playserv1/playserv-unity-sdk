using System;

namespace Playserv.DataSubscription.Exceptions
{
    public sealed class SubscriptionTerminatedException : DataSubscriptionException
    {
        public const int Code = 49001;

        public long SubscriptionId { get; }

        public SubscriptionTerminatedException(long subscriptionId, string message = null)
            : base(Code, message ?? "Subscription terminated - entity deleted")
        {
            SubscriptionId = subscriptionId;
        }
    }
}
