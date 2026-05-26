using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Events
{
    internal sealed class EventSubscriptionManager
    {
        private readonly Dictionary<string, string> _eventTypeToSubscriptionId = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _subscriptionIdToEventType = new Dictionary<string, string>();
        private readonly List<string> _pendingEventTypes = new List<string>();
        private readonly Dictionary<Type, List<IObserverRegistration>> _typeObservers = new Dictionary<Type, List<IObserverRegistration>>();
        private readonly Dictionary<string, List<IObserver<string>>> _rawObserversByEventType =
            new Dictionary<string, List<IObserver<string>>>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public void AddSubscription(string eventType, string subscriptionId)
        {
            lock (_lock)
            {
                _eventTypeToSubscriptionId[eventType] = subscriptionId;
                _subscriptionIdToEventType[subscriptionId] = eventType;
            }
        }

        public void RemoveSubscription(string eventType)
        {
            lock (_lock)
            {
                if (_eventTypeToSubscriptionId.TryGetValue(eventType, out var subscriptionId))
                {
                    _eventTypeToSubscriptionId.Remove(eventType);
                    _subscriptionIdToEventType.Remove(subscriptionId);
                }
            }
        }

        public string GetSubscriptionId(string eventType)
        {
            lock (_lock)
            {
                return _eventTypeToSubscriptionId.TryGetValue(eventType, out var id) ? id : null;
            }
        }

        public string GetTopic(string subscriptionId)
        {
            lock (_lock)
            {
                return _subscriptionIdToEventType.TryGetValue(subscriptionId, out var eventType) ? eventType : null;
            }
        }

        public bool TryBindOrphanSubscriptionToSingleObservedTopic(string subscriptionId, out string eventType)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                eventType = null;
                return false;
            }

            lock (_lock)
            {
                if (_subscriptionIdToEventType.TryGetValue(subscriptionId, out var existingTopic))
                {
                    eventType = existingTopic;
                    return true;
                }

                var candidates = GetObservedTopicsWithoutSubscriptionLocked(2);

                if (candidates.Length != 1)
                {
                    eventType = null;
                    return false;
                }

                eventType = candidates[0];
                _eventTypeToSubscriptionId[eventType] = subscriptionId;
                _subscriptionIdToEventType[subscriptionId] = eventType;
                return true;
            }
        }

        public void AddPendingSubscription(string eventType)
        {
            lock (_lock)
            {
                if (!_pendingEventTypes.Contains(eventType))
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

        public string GetFirstPendingTopic()
        {
            lock (_lock)
            {
                return _pendingEventTypes.Count > 0 ? _pendingEventTypes[0] : null;
            }
        }

        public IReadOnlyList<string> GetAllTopics()
        {
            lock (_lock)
            {
                return _eventTypeToSubscriptionId.Keys.ToList();
            }
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
                if (_rawObserversByEventType.TryGetValue(eventType, out var rawObservers) && rawObservers.Count > 0)
                    return true;

                foreach (var pair in _typeObservers)
                {
                    if (pair.Value.Count == 0)
                        continue;

                    if (string.Equals(EventTypeRegistry.GetCanonicalName(pair.Key), eventType, StringComparison.Ordinal))
                        return true;
                }

                return false;
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

        private string[] GetObservedTopicsWithoutSubscriptionLocked(int limit)
        {
            var candidates = new List<string>(limit);
            foreach (var type in _typeObservers.Keys)
            {
                AddCandidateLocked(candidates, EventTypeRegistry.GetCanonicalName(type), limit);
                if (candidates.Count >= limit)
                    return candidates.ToArray();
            }

            foreach (var eventType in _rawObserversByEventType.Keys)
            {
                AddCandidateLocked(candidates, eventType, limit);
                if (candidates.Count >= limit)
                    return candidates.ToArray();
            }

            return candidates.ToArray();
        }

        private void AddCandidateLocked(List<string> candidates, string eventType, int limit)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return;

            if (_eventTypeToSubscriptionId.ContainsKey(eventType) || _pendingEventTypes.Contains(eventType))
                return;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i], eventType, StringComparison.Ordinal))
                    return;
            }

            if (candidates.Count < limit)
                candidates.Add(eventType);
        }

        private interface IObserverRegistration
        {
            bool Matches(object observer);

            void OnNext(object eventData);
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
        }
    }
}
