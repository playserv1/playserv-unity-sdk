using System;

namespace Playserv.DataSubscription.Requests
{
    /// <summary>
    /// Request to force full-state refresh for existing subscription.
    /// </summary>
    [Serializable]
    public sealed class DataSubscriptionRefreshRequest
    {
        /// <summary>
        /// Client-generated request id.
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// Existing subscription id to refresh.
        /// </summary>
        public long SubscriptionId { get; set; }
    }
}
