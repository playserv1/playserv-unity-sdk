using System;

namespace Playserv.DataSubscription.Responses
{
    /// <summary>
    /// Response for data subscription open request.
    /// </summary>
    [Serializable]
    public class DataSubscriptionResponse
    {
        /// <summary>
        /// Request id provided by client.
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// Successful result payload.
        /// </summary>
        public DataSubscriptionResult Result { get; set; }

        /// <summary>
        /// Error payload when request failed.
        /// </summary>
        public DataSubscriptionError Error { get; set; }

        /// <summary>
        /// True when response contains error.
        /// </summary>
        public bool HasError => Error != null;
    }

    /// <summary>
    /// Success payload for opened data subscription.
    /// </summary>
    [Serializable]
    public class DataSubscriptionResult
    {
        /// <summary>
        /// Created subscription id.
        /// </summary>
        public long SubscriptionId { get; set; }
    }

    /// <summary>
    /// Error payload for data subscription request.
    /// </summary>
    [Serializable]
    public class DataSubscriptionError
    {
        /// <summary>
        /// Numeric error code.
        /// </summary>
        public int ErrorCode { get; set; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>Optional retryability supplied by the generic command error envelope.</summary>
        public bool? Retryable { get; set; }

        public string SourceCode { get; set; }

        public string RawDetails { get; set; }
    }
}
