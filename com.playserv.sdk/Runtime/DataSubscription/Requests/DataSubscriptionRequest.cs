using System;
using System.Collections.Generic;

namespace Playserv.DataSubscription.Requests
{
    [Serializable]
    public sealed class DataSubscriptionRequest
    {
        public long RequestId;
        public string Query;
        public Dictionary<string, object> Variables;

        public DataSubscriptionRequest(long requestId, string query, Dictionary<string, object> variables)
        {
            RequestId = requestId;
            Query = query;
            Variables = variables;
        }
    }
}
