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

                var candidates = _typeObservers.Keys
                    .Select(EventTypeRegistry.GetCanonicalName)
                    .Distinct(StringComparer.Ordinal)
                    .Where(topic =>
                        !_eventTypeToSubscriptionId.ContainsKey(topic) &&
                        !_pendingEventTypes.Contains(topic))
                    .Take(2)
                    .ToArray();

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

        public bool HasObservers<T>()
        {
            lock (_lock)
            {
                return _typeObservers.TryGetValue(typeof(T), out var observers) && observers.Count > 0;
            }
        }

        public void NotifyEvent<T>(T eventData)
        {
            NotifyEvent(typeof(T), eventData);
        }

        public void NotifyEvent(Type eventType, object eventData)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));

            List<IObserverRegistration> observers;
            lock (_lock)
            {
                if (!_typeObservers.TryGetValue(eventType, out var observerList))
                    return;

                observers = observerList.ToList();
            }

            foreach (var observer in observers)
            {
                observer.OnNext(eventData);
            }
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
