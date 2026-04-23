using Playserv.Events;
using Playserv.Events.Requests;
using Playserv.Events.Responses;

namespace Playserv.Proxy.Common
{
    internal static class EventCommandTypeRegistration
    {
        public static void Register(CommandTypeRegistryBuilder builder)
        {
            builder.Register<EventMessage>("BroadcastEvent");
            builder.Register<GroupEventMessage>();
            builder.Register<UserEventMessage>();
            builder.Register<EventSubscribeRequest>();
            builder.Register<EventSubscribeResponse>();
            builder.Register<EventSubscribedMessage>();
            builder.Register<EventUnsubscribeRequest>();
            builder.Register<EventUnsubscribeResponse>();
            builder.Register<SubscribeGroupRequest>();
            builder.Register<SubscribeGroupResponse>();
            builder.Register<UnsubscribeGroupRequest>();
            builder.Register<UnsubscribeGroupResponse>();
            builder.Register<ErrorResponse>();
        }
    }
}
