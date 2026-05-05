#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Collections.Generic;

namespace Playserv.DataSubscription.Requests
{
    /// <summary>
    /// Request to open data subscription for query and variables.
    /// </summary>
    [Serializable]
    public sealed class DataSubscriptionRequest
    {
        /// <summary>
        /// Client-generated request id.
        /// </summary>
        public long RequestId;

        /// <summary>
        /// Dataflow query string.
        /// </summary>
        public string Query;

        /// <summary>
        /// Query variables.
        /// </summary>
        public Dictionary<string, object> Variables;

        /// <summary>
        /// Creates data subscription request.
        /// </summary>
        /// <param name="requestId">Client request id.</param>
        /// <param name="query">Query string.</param>
        /// <param name="variables">Query variables.</param>
        public DataSubscriptionRequest(long requestId, string query, Dictionary<string, object> variables)
        {
            RequestId = requestId;
            Query = query;
            Variables = variables;
        }
    }
}

#endif
