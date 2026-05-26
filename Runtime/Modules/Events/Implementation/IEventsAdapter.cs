using System;
using System.Threading;
using System.Threading.Tasks;

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
        /// Subscribes to events of type <typeparamref name="T"/> and emits the raw JSON payload.
        /// This is intended for high-frequency events where the game wants to parse only selected fields.
        /// </summary>
        /// <typeparam name="T">Event payload type used only to resolve the event topic.</typeparam>
        /// <returns>Observable stream of raw JSON payload strings.</returns>
        IObservable<string> SubscribeRaw<T>();

        /// <summary>
        /// Subscribes to events of type <typeparamref name="T"/> using callback.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="onNext">Callback executed when event is received.</param>
        /// <returns>Disposable subscription handle.</returns>
        IDisposable Subscribe<T>(Action<T> onNext);

        /// <summary>
        /// Subscribes to events of type <typeparamref name="T"/> using a raw JSON payload callback.
        /// </summary>
        /// <typeparam name="T">Event payload type used only to resolve the event topic.</typeparam>
        /// <param name="onNext">Callback executed with raw JSON payload when event is received.</param>
        /// <returns>Disposable subscription handle.</returns>
        IDisposable SubscribeRaw<T>(Action<string> onNext);

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

        /// <summary>
        /// Joins named event group for group-scoped routing.
        /// </summary>
        /// <param name="groupName">Target group name.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>True when group join succeeded.</returns>
        Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default);

        /// <summary>
        /// Leaves named event group.
        /// </summary>
        /// <param name="groupName">Target group name.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>True when group leave succeeded.</returns>
        Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default);
    }
}
