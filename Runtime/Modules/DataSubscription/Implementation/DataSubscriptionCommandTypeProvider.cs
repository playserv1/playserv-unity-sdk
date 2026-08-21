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
            builder.Register<DataSubscriptionCloseRequest>();
            builder.Register<DataSubscriptionResponse>();
            builder.Register<DataSubscriptionCloseResponse>();
            builder.Register<DataSubscriptionUpdate>();
        }
    }
}
