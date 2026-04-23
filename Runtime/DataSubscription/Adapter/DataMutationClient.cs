using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playserv.DataSubscription.Requests;
using Playserv.Proxy.Common;

namespace Playserv.DataSubscription
{
    internal sealed class DataMutationClient
    {
        private readonly PlayServImplementation _transport;
        private readonly DataSubscriptionRequestIdSource _requestIds;

        public DataMutationClient(PlayServImplementation transport, DataSubscriptionRequestIdSource requestIds)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
        }

        public void SendMutation(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            _ = SendMutationAsync(subscriptionId, query, variables, patch);
        }

        public async Task SendMutationAsync(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            var mutationRequest = new DataMutationRequest
            {
                RequestId = _requestIds.Next(),
                Query = query,
                Variables = DataSubscriptionRequestSupport.CloneVariables(variables),
                UpdateType = "Overwrite",
                Data = patch
            };

            await _transport.SendAsync(mutationRequest, "module_dataflow");
        }
    }
}
