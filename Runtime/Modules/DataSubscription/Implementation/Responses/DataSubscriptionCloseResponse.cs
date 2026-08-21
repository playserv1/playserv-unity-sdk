using System;

namespace Playserv.DataSubscription.Responses
{
    [Serializable]
    public sealed class DataSubscriptionCloseResponse
    {
        public long RequestId { get; set; }

        public DataSubscriptionCloseResult Result { get; set; }

        public DataSubscriptionError Error { get; set; }

        public bool HasError => Error != null;
    }

    [Serializable]
    public sealed class DataSubscriptionCloseResult
    {
        public bool Success { get; set; }
    }
}
