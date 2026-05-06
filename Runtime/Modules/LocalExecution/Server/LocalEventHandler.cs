#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_EVENTS && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
using System;
using System.Collections.Generic;

namespace Playserv.Server
{
    /// <summary>
    /// In-process event bus for PlayServ publish/subscribe API.
    /// </summary>
    public sealed class LocalEventHandler : IEventHandler
    {
        private readonly object _sync = new();
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

        /// <summary>
        /// Publishes event to all local subscribers of event type.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="event">Event payload.</param>
        /// <returns>Always true because event is handled locally.</returns>
        public bool TryPublish<T>(T @event)
        {
            List<Delegate> handlers = null;
            lock (_sync)
            {
                if (_subscribers.TryGetValue(typeof(T), out var list) && list.Count > 0)
                    handlers = new List<Delegate>(list);
            }

            if (handlers == null)
                return true;

            for (var i = 0; i < handlers.Count; i++)
            {
                if (handlers[i] is Action<T> handler)
                    handler(@event);
            }

            return true;
        }

        /// <summary>
        /// Publishes group-scoped event in local mode.
        /// Group info is currently ignored and event is dispatched by type.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="groupName">Group name.</param>
        /// <param name="event">Event payload.</param>
        /// <returns>Always true because event is handled locally.</returns>
        public bool TryPublishForGroup<T>(string groupName, T @event)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name is required.", nameof(groupName));

            return TryPublish(@event);
        }

        /// <summary>
        /// Publishes user-scoped event in local mode.
        /// User info is currently ignored and event is dispatched by type.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="userId">User ID.</param>
        /// <param name="event">Event payload.</param>
        /// <returns>Always true because event is handled locally.</returns>
        public bool TryPublishForUser<T>(string userId, T @event)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID is required.", nameof(userId));

            return TryPublish(@event);
        }

        /// <summary>
        /// Creates callback subscription in local event bus.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="onNext">Event callback.</param>
        /// <param name="subscription">Disposable subscription handle.</param>
        /// <returns>Always true because subscription is handled locally.</returns>
        public bool TrySubscribe<T>(Action<T> onNext, out IDisposable subscription)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            lock (_sync)
            {
                if (!_subscribers.TryGetValue(typeof(T), out var list))
                {
                    list = new List<Delegate>();
                    _subscribers[typeof(T)] = list;
                }

                list.Add(onNext);
            }

            subscription = new Subscription(() => RemoveSubscriber(typeof(T), onNext));
            return true;
        }

        /// <summary>
        /// Creates observable subscription in local event bus.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="observable">Observable stream.</param>
        /// <returns>Always true because subscription is handled locally.</returns>
        public bool TrySubscribe<T>(out IObservable<T> observable)
        {
            observable = new LocalObservable<T>(this);
            return true;
        }

        private void RemoveSubscriber(Type eventType, Delegate handler)
        {
            lock (_sync)
            {
                if (!_subscribers.TryGetValue(eventType, out var list))
                    return;

                list.Remove(handler);
                if (list.Count == 0)
                    _subscribers.Remove(eventType);
            }
        }

        private sealed class LocalObservable<T> : IObservable<T>
        {
            private readonly LocalEventHandler _owner;

            public LocalObservable(LocalEventHandler owner)
            {
                _owner = owner;
            }

            public IDisposable Subscribe(IObserver<T> observer)
            {
                if (observer == null)
                    throw new ArgumentNullException(nameof(observer));

                _owner.TrySubscribe<T>(observer.OnNext, out var subscription);
                return subscription;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;
            private bool _disposed;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _dispose();
            }
        }
    }
}

#endif
