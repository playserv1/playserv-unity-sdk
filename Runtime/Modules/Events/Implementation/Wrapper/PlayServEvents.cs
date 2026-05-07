using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Event-oriented PlayServ SDK surface.
    /// </summary>
    public static partial class PlayServEvents
    {
        private static readonly IPlayServEventsApi Api = new PlayServApiEventsFacade();

        public static IObservable<T> Subscribe<T>() => Api.Subscribe<T>();

        public static IDisposable Subscribe<T>(Action<T> onNext) => Api.Subscribe(onNext);

        public static void Publish<T>(T @event) => Api.Publish(@event);

        public static void PublishForGroup<T>(string groupName, T @event) => Api.PublishForGroup(groupName, @event);

        public static void PublishForUser<T>(string userId, T @event) => Api.PublishForUser(userId, @event);

        public static Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            Api.SubscribeGroupAsync(groupName, ct);

        public static Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            Api.UnsubscribeGroupAsync(groupName, ct);
    }
}
