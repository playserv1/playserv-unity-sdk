using System;
using System.Collections.Generic;

namespace Playserv.DataSubscription.Requests
{
    [Serializable]
    public sealed class DataMutationRequest
    {
        public long RequestId { get; set; }

        public string Query { get; set; }

        public Dictionary<string, object> Variables { get; set; }

        public string UpdateType { get; set; } = "Overwrite";

        public object Data { get; set; }
    }
}
