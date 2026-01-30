using System;

namespace Playserv.DataSubscription.Requests
{
    [Serializable]
    public sealed class DataSubscriptionRefreshRequest
    {
        public long RequestId { get; set; }
        public long SubscriptionId { get; set; }
    }
}
