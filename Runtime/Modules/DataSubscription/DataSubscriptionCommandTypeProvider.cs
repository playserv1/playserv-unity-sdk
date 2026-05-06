#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Common;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionCommandTypeProvider : ICommandTypeProvider
    {
        public void RegisterCommandTypes(CommandTypeRegistryBuilder builder)
        {
            builder.Register<DataGetRequest>();
            builder.Register<DataGetResponse>();
            builder.Register<DataMutationRequest>();
            builder.Register<DataMutationResponse>();
            builder.Register<DataSubscriptionRequest>();
            builder.Register<DataSubscriptionRefreshRequest>();
            builder.Register<DataSubscriptionResponse>();
            builder.Register<DataSubscriptionUpdate>();
        }
    }
}

#endif
