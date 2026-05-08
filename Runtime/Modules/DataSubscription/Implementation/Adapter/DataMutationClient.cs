using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playserv.DataSubscription.Requests;
using Playserv.Modules;

namespace Playserv.DataSubscription
{
    internal sealed class DataMutationClient
    {
        private readonly IPlayServCommandBus _commandBus;
        private readonly DataSubscriptionRequestIdSource _requestIds;

        public DataMutationClient(IPlayServCommandBus commandBus, DataSubscriptionRequestIdSource requestIds)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
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

            await _commandBus.SendAsync(mutationRequest, "module_dataflow");
        }
    }
}
