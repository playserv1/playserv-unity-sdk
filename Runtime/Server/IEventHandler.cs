using System;

namespace Playserv.Server
{
    /// <summary>
    /// Contract for local event publish/subscribe handling used by PlayServ events API.
    /// </summary>
    public interface IEventHandler
    {
        /// <summary>
        /// Publishes global event in local context.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="event">Event payload.</param>
        /// <returns>True when handled locally; otherwise false.</returns>
        bool TryPublish<T>(T @event);

        /// <summary>
        /// Publishes group-scoped event in local context.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="groupName">Group name.</param>
        /// <param name="event">Event payload.</param>
        /// <returns>True when handled locally; otherwise false.</returns>
        bool TryPublishForGroup<T>(string groupName, T @event);

        /// <summary>
        /// Publishes user-scoped event in local context.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="userId">User ID.</param>
        /// <param name="event">Event payload.</param>
        /// <returns>True when handled locally; otherwise false.</returns>
        bool TryPublishForUser<T>(string userId, T @event);

        /// <summary>
        /// Tries to create callback-based subscription in local context.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="onNext">Callback invoked on event.</param>
        /// <param name="subscription">Disposable subscription handle when handled.</param>
        /// <returns>True when handled locally; otherwise false.</returns>
        bool TrySubscribe<T>(Action<T> onNext, out IDisposable subscription);

        /// <summary>
        /// Tries to create observable subscription in local context.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="observable">Observable stream when handled.</param>
        /// <returns>True when handled locally; otherwise false.</returns>
        bool TrySubscribe<T>(out IObservable<T> observable);
    }
}
