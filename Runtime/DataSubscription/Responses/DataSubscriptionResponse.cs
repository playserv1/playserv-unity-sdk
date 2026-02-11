using System;

namespace Playserv.DataSubscription.Responses
{
    [Serializable]
    public class DataSubscriptionResponse
    {
        public long RequestId { get; set; }
        public DataSubscriptionResult Result { get; set; }
        public DataSubscriptionError Error { get; set; }
        public bool HasError => Error != null;
    }

    [Serializable]
    public class DataSubscriptionResult
    {
        public long SubscriptionId { get; set; }
    }

    [Serializable]
    public class DataSubscriptionError
    {
        public int ErrorCode { get; set; }
        public string Message { get; set; }
    }
}
