using Playserv.Events.Requests;
using Playserv.Events.Responses;
using Playserv.Proxy.Common;

namespace Playserv.Events
{
    internal sealed class EventCommandTypeProvider : ICommandTypeProvider
    {
        public void RegisterCommandTypes(CommandTypeRegistryBuilder builder)
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
        }
    }
}
