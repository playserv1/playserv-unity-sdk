#if !UNITY_5_3_OR_NEWER && !PLAYSERV_MODULE_DISABLED_EVENTS
using System;

namespace Playserv.Wrapper
{
    public static partial class PlayServ
    {
        public static IObservable<T> Subscribe<T>() =>
            PlayServEvents.Subscribe<T>();

        public static IDisposable Subscribe<T>(Action<T> onNext) =>
            PlayServEvents.Subscribe(onNext);

        public static void Publish<T>(T @event) =>
            PlayServEvents.Publish(@event);

        public static void PublishForGroup<T>(string groupName, T @event) =>
            PlayServEvents.PublishForGroup(groupName, @event);

        public static void PublishForUser<T>(string userId, T @event) =>
            PlayServEvents.PublishForUser(userId, @event);

        public static void SetCommandHandler(Playserv.Server.ICommandHandler commandHandler) =>
            PlayServServerRpc.SetCommandHandler(commandHandler);

        public static void SetEventHandler(Playserv.Server.IEventHandler eventHandler) =>
            PlayServServerRpc.SetEventHandler(eventHandler);

        public static void SetRpcInvoker(Playserv.RPC.IRpcInvoker rpcInvoker) =>
            PlayServServerRpc.SetRpcInvoker(rpcInvoker);
    }
}
#endif
