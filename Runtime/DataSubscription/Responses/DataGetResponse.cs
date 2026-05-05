#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.DataSubscription.Responses
{
    /// <summary>
    /// Response for simplified DataGetRequest command.
    /// </summary>
    [Serializable]
    public sealed class DataGetResponse
    {
        /// <summary>
        /// Request id provided by client.
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// Successful response payload.
        /// </summary>
        public DataGetResult Result { get; set; }

        /// <summary>
        /// Error payload if request failed.
        /// </summary>
        public DataGetError Error { get; set; }

        /// <summary>
        /// True when response contains error.
        /// </summary>
        public bool HasError => Error != null;
    }

    /// <summary>
    /// Successful data payload for key retrieval.
    /// </summary>
    [Serializable]
    public sealed class DataGetResult
    {
        /// <summary>
        /// Returned data object from server.
        /// </summary>
        public object Data { get; set; }
    }

    /// <summary>
    /// Error payload for key retrieval.
    /// </summary>
    [Serializable]
    public sealed class DataGetError
    {
        private int _code;

        /// <summary>
        /// Numeric error code.
        /// </summary>
        public int Code
        {
            get => _code;
            set => _code = value;
        }

        /// <summary>
        /// Compatibility alias for backends using ErrorCode field.
        /// </summary>
        public int ErrorCode
        {
            get => _code;
            set => _code = value;
        }

        /// <summary>
        /// Human-readable message.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}

#endif
