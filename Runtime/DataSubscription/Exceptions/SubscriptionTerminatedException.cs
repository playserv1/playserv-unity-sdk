#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
namespace Playserv.DataSubscription.Exceptions
{
    /// <summary>
    /// Indicates that backend terminated active subscription.
    /// </summary>
    public sealed class SubscriptionTerminatedException : DataSubscriptionException
    {
        /// <summary>
        /// Error code used by backend for terminated subscription.
        /// </summary>
        public const int Code = 49001;

        /// <summary>
        /// Terminated subscription id.
        /// </summary>
        public long SubscriptionId { get; }

        /// <summary>
        /// Creates subscription terminated exception.
        /// </summary>
        /// <param name="subscriptionId">Terminated subscription id.</param>
        /// <param name="message">Optional custom message.</param>
        public SubscriptionTerminatedException(long subscriptionId, string message = null)
            : base(Code, message ?? "Subscription terminated - entity deleted")
        {
            SubscriptionId = subscriptionId;
        }
    }
}

#endif
