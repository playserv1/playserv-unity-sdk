using System;
using System.Collections.Generic;

namespace Playserv.DataSubscription.Requests
{
    /// <summary>
    /// Simplified one-time data retrieval request by key.
    /// </summary>
    [Serializable]
    public sealed class DataGetRequest
    {
        /// <summary>
        /// Client-generated request id.
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// Entity key used for direct lookup.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// GraphQL-like query string.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Query variables payload.
        /// </summary>
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }
}
