namespace Playserv.DataSubscription
{
    /// <summary>
    /// Selects backend strategy for shared-entity subscription.
    /// </summary>
    public enum DataSubscriptionMode
    {
        /// <summary>
        /// Use transport DataSubscriptionRequest/DataSubscriptionUpdate only.
        /// </summary>
        Transport = 0,

        /// <summary>
        /// Use polling DataGet loop only.
        /// </summary>
        Polling = 1
    }
}
