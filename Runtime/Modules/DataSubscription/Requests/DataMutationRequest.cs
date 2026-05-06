using System;
using System.Collections.Generic;

namespace Playserv.DataSubscription.Requests
{
    /// <summary>
    /// Request to mutate entity data for active subscription query.
    /// </summary>
    [Serializable]
    public sealed class DataMutationRequest
    {
        /// <summary>
        /// Client-generated request id.
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// Query string used by mutation target.
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Variables used by mutation target query.
        /// </summary>
        public Dictionary<string, object> Variables { get; set; }

        /// <summary>
        /// Update type name. Default is "Overwrite".
        /// </summary>
        public string UpdateType { get; set; } = "Overwrite";

        /// <summary>
        /// Mutation payload (full object or patch).
        /// </summary>
        public object Data { get; set; }
    }
}
