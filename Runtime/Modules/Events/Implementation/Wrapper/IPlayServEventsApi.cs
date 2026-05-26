using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Wrapper
{
    public interface IPlayServEventsApi
    {
        IObservable<T> Subscribe<T>();

        IObservable<string> SubscribeRaw<T>();

        IDisposable Subscribe<T>(Action<T> onNext);

        IDisposable SubscribeRaw<T>(Action<string> onNext);

        void Publish<T>(T @event);

        void PublishForGroup<T>(string groupName, T @event);

        void PublishForUser<T>(string userId, T @event);

        Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default);

        Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default);
    }
}
