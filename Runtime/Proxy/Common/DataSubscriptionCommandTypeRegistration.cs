#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using Playserv.DataSubscription;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;

namespace Playserv.Proxy.Common
{
    internal static class DataSubscriptionCommandTypeRegistration
    {
        public static void Register(CommandTypeRegistryBuilder builder)
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
