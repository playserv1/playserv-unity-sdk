using System;

namespace Playserv.Events
{
    /// <summary>
    /// Contract for event publish/subscribe operations over PlayServ transport.
    /// </summary>
    public interface IEventsAdapter
    {
        /// <summary>
        /// Subscribes to events of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <returns>Observable stream of events.</returns>
        IObservable<T> Subscribe<T>();

        /// <summary>
        /// Subscribes to events of type <typeparamref name="T"/> using callback.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="onNext">Callback executed when event is received.</param>
        /// <returns>Disposable subscription handle.</returns>
        IDisposable Subscribe<T>(Action<T> onNext);

        /// <summary>
        /// Publishes global event.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="event">Event payload.</param>
        void Publish<T>(T @event);

        /// <summary>
        /// Publishes event for target group.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="groupName">Target group name.</param>
        /// <param name="event">Event payload.</param>
        void PublishForGroup<T>(string groupName, T @event);

        /// <summary>
        /// Publishes event for target user.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="userId">Target user id.</param>
        /// <param name="event">Event payload.</param>
        void PublishForUser<T>(string userId, T @event);
    }
}
