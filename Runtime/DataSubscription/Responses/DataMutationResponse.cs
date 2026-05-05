#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.DataSubscription.Responses
{
    /// <summary>
    /// Response for data mutation request.
    /// </summary>
    [Serializable]
    public sealed class DataMutationResponse
    {
        /// <summary>
        /// Request id provided by client.
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// Mutation result payload.
        /// </summary>
        public DataMutationResult Result { get; set; }
    }

    /// <summary>
    /// Mutation execution result.
    /// </summary>
    [Serializable]
    public sealed class DataMutationResult
    {
        /// <summary>
        /// True when mutation succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Optional details payload.
        /// </summary>
        public DataMutationDetails Details { get; set; }

        /// <summary>
        /// Optional error payload.
        /// </summary>
        public DataMutationError Error { get; set; }
    }

    /// <summary>
    /// Additional mutation details.
    /// </summary>
    [Serializable]
    public sealed class DataMutationDetails
    {
        // OldValues comes as an arbitrary JSON object
        /// <summary>
        /// Previous values before mutation was applied.
        /// </summary>
        public object OldValues { get; set; }
    }

    /// <summary>
    /// Mutation error payload.
    /// </summary>
    [Serializable]
    public sealed class DataMutationError
    {
        /// <summary>
        /// Numeric error code.
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Optional raw error payload from backend.
        /// </summary>
        public object Payload { get; set; }
    }
}

#endif
