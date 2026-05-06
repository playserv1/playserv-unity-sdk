#if !PLAYSERV_MODULE_DISABLED_EVENTS
using System;
using System.Threading;
using System.Threading.Tasks;
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
using Playserv.Server;
#endif

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

        public static Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            PlayServEvents.SubscribeGroupAsync(groupName, ct);

        public static Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            PlayServEvents.UnsubscribeGroupAsync(groupName, ct);

#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        public static void SetEventHandler(IEventHandler eventHandler) =>
            PlayServEvents.SetEventHandler(eventHandler);
#endif
    }
}
#endif
