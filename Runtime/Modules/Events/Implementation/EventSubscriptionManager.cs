using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Events
{
    internal sealed class EventSubscriptionManager
    {
        private readonly HashSet<string> _activeEventTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingEventTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<Type, List<IObserverRegistration>> _typeObservers = new Dictionary<Type, List<IObserverRegistration>>();
        private readonly Dictionary<string, List<IObserver<string>>> _rawObserversByEventType =
            new Dictionary<string, List<IObserver<string>>>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public void MarkSubscriptionActive(string eventType)
        {
            lock (_lock)
            {
                _pendingEventTypes.Remove(eventType);
                if (HasObserversForEventTypeLocked(eventType))
                    _activeEventTypes.Add(eventType);
            }
        }

        public void RemoveSubscription(string eventType)
        {
            lock (_lock)
            {
                _activeEventTypes.Remove(eventType);
                _pendingEventTypes.Remove(eventType);
            }
        }

        public bool IsSubscribed(string eventType)
        {
            lock (_lock)
            {
                return _activeEventTypes.Contains(eventType);
            }
        }

        public void ResetConnectionSubscriptions()
        {
            lock (_lock)
            {
                _activeEventTypes.Clear();
                _pendingEventTypes.Clear();
            }
        }

        public void AddPendingSubscription(string eventType)
        {
            lock (_lock)
            {
                _pendingEventTypes.Add(eventType);
            }
        }

        public void RemovePendingSubscription(string eventType)
        {
            lock (_lock)
            {
                _pendingEventTypes.Remove(eventType);
            }
        }

        public bool IsSubscriptionPending(string eventType)
        {
            lock (_lock)
            {
                return _pendingEventTypes.Contains(eventType);
            }
        }

        public IReadOnlyList<string> GetAllTopics()
        {
            lock (_lock)
            {
                return _activeEventTypes.ToList();
            }
        }

        /// <summary>
        /// Every topic this client should be subscribed to on the CURRENT connection: topics with a
        /// live subscription id, topics still pending a response, and topics that have observers but
        /// lost their request (e.g. it died with the previous connection). Used to replay the
        /// server-side (connection-scoped) subscriptions after a transparent reconnect.
        /// </summary>
        public IReadOnlyList<string> GetTopicsForResubscribe()
        {
            lock (_lock)
            {
                var topics = new List<string>();

                foreach (var eventType in _activeEventTypes)
                    AddTopicOnce(topics, eventType);

                foreach (var eventType in _pendingEventTypes)
                    AddTopicOnce(topics, eventType);

                foreach (var pair in _typeObservers)
                {
                    if (pair.Value.Count > 0)
                        AddTopicOnce(topics, EventTypeRegistry.GetCanonicalName(pair.Key));
                }

                foreach (var pair in _rawObserversByEventType)
                {
                    if (pair.Value.Count > 0)
                        AddTopicOnce(topics, pair.Key);
                }

                return topics;
            }
        }

        private static void AddTopicOnce(List<string> topics, string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return;

            for (int i = 0; i < topics.Count; i++)
            {
                if (string.Equals(topics[i], eventType, StringComparison.Ordinal))
                    return;
            }

            topics.Add(eventType);
        }

        public void AddObserver<T>(IObserver<T> observer)
        {
            lock (_lock)
            {
                var type = typeof(T);
                if (!_typeObservers.TryGetValue(type, out var observers))
                {
                    observers = new List<IObserverRegistration>();
                    _typeObservers[type] = observers;
                }

                observers.Add(new ObserverRegistration<T>(observer));
            }
        }

        public void AddRawObserver(string eventType, IObserver<string> observer)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Event type is required.", nameof(eventType));

            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (_lock)
            {
                if (!_rawObserversByEventType.TryGetValue(eventType, out var observers))
                {
                    observers = new List<IObserver<string>>();
                    _rawObserversByEventType[eventType] = observers;
                }

                observers.Add(observer);
            }
        }

        public void RemoveObserver<T>(IObserver<T> observer)
        {
            lock (_lock)
            {
                var type = typeof(T);
                if (_typeObservers.TryGetValue(type, out var observers))
                {
                    observers.RemoveAll(x => x.Matches(observer));
                    if (observers.Count == 0)
                    {
                        _typeObservers.Remove(type);
                    }
                }
            }
        }

        public void RemoveRawObserver(string eventType, IObserver<string> observer)
        {
            if (string.IsNullOrWhiteSpace(eventType) || observer == null)
                return;

            lock (_lock)
            {
                if (_rawObserversByEventType.TryGetValue(eventType, out var observers))
                {
                    observers.RemoveAll(x => ReferenceEquals(x, observer));
                    if (observers.Count == 0)
                        _rawObserversByEventType.Remove(eventType);
                }
            }
        }

        public bool HasObservers<T>()
        {
            lock (_lock)
            {
                return _typeObservers.TryGetValue(typeof(T), out var observers) && observers.Count > 0;
            }
        }

        public bool HasTypedObservers(Type eventType)
        {
            if (eventType == null)
                return false;

            lock (_lock)
            {
                return _typeObservers.TryGetValue(eventType, out var observers) && observers.Count > 0;
            }
        }

        public bool HasObserversForEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return false;

            lock (_lock)
            {
                return HasObserversForEventTypeLocked(eventType);
            }
        }

        public void NotifySubscriptionError(string eventType, Exception exception)
        {
            List<IObserverRegistration> typedObservers = null;
            List<IObserver<string>> rawObservers = null;

            lock (_lock)
            {
                _activeEventTypes.Remove(eventType);
                _pendingEventTypes.Remove(eventType);

                foreach (var pair in _typeObservers.ToArray())
                {
                    if (!string.Equals(EventTypeRegistry.GetCanonicalName(pair.Key), eventType, StringComparison.Ordinal))
                        continue;

                    typedObservers = typedObservers ?? new List<IObserverRegistration>();
                    typedObservers.AddRange(pair.Value);
                    _typeObservers.Remove(pair.Key);
                }

                if (_rawObserversByEventType.TryGetValue(eventType, out var registeredRaw))
                {
                    rawObservers = registeredRaw.ToList();
                    _rawObserversByEventType.Remove(eventType);
                }
            }

            if (typedObservers != null)
            {
                foreach (var observer in typedObservers)
                    observer.OnError(exception);
            }

            if (rawObservers != null)
            {
                foreach (var observer in rawObservers)
                    observer.OnError(exception);
            }
        }

        public void NotifyEvent<T>(T eventData)
        {
            NotifyEvent(typeof(T), eventData);
        }

        public bool NotifyRawEvent(string eventType, string payload)
        {
            IObserver<string> singleObserver = null;
            List<IObserver<string>> observers = null;
            lock (_lock)
            {
                if (!_rawObserversByEventType.TryGetValue(eventType, out var observerList))
                    return false;

                if (observerList.Count == 1)
                    singleObserver = observerList[0];
                else
                    observers = observerList.ToList();
            }

            if (singleObserver != null)
            {
                singleObserver.OnNext(payload);
                return true;
            }

            foreach (var observer in observers)
            {
                observer.OnNext(payload);
            }

            return true;
        }

        public void NotifyEvent(Type eventType, object eventData)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));

            IObserverRegistration singleObserver = null;
            List<IObserverRegistration> observers = null;
            lock (_lock)
            {
                if (!_typeObservers.TryGetValue(eventType, out var observerList))
                    return;

                if (observerList.Count == 1)
                    singleObserver = observerList[0];
                else
                    observers = observerList.ToList();
            }

            if (singleObserver != null)
            {
                singleObserver.OnNext(eventData);
                return;
            }

            foreach (var observer in observers)
            {
                observer.OnNext(eventData);
            }
        }

        private bool HasObserversForEventTypeLocked(string eventType)
        {
            if (_rawObserversByEventType.TryGetValue(eventType, out var rawObservers) && rawObservers.Count > 0)
                return true;

            foreach (var pair in _typeObservers)
            {
                if (pair.Value.Count > 0 &&
                    string.Equals(EventTypeRegistry.GetCanonicalName(pair.Key), eventType, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private interface IObserverRegistration
        {
            bool Matches(object observer);

            void OnNext(object eventData);

            void OnError(Exception exception);
        }

        private sealed class ObserverRegistration<T> : IObserverRegistration
        {
            private readonly IObserver<T> _observer;

            public ObserverRegistration(IObserver<T> observer)
            {
                _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            }

            public bool Matches(object observer)
            {
                return ReferenceEquals(_observer, observer);
            }

            public void OnNext(object eventData)
            {
                _observer.OnNext((T)eventData);
            }

            public void OnError(Exception exception)
            {
                _observer.OnError(exception);
            }
        }
    }
}
