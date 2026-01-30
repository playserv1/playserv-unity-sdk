using System;

namespace Playserv.Events
{
    public interface IEventsAdapter
    {
        IObservable<T> Subscribe<T>();
        IDisposable Subscribe<T>(Action<T> onNext);
        void Publish<T>(T @event);
        void PublishForGroup<T>(string groupName, T @event);
        void PublishForUser<T>(string userId, T @event);
    }
}