using System;

namespace Playserv.DataSubscription.Requests
{
    [Serializable]
    public sealed class DataSubscriptionCloseRequest
    {
        public long RequestId { get; set; }

        public long SubscriptionId { get; set; }
    }
}
