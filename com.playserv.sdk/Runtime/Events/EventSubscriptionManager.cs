using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.Events
{
    internal sealed class EventSubscriptionManager
    {
        private readonly Dictionary<string, string> _eventTypeToSubscriptionId = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _subscriptionIdToEventType = new Dictionary<string, string>();
        private readonly HashSet<string> _pendingEventTypes = new HashSet<string>();
        private readonly Dictionary<Type, List<object>> _typeObservers = new Dictionary<Type, List<object>>();
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

        public string GetFirstPendingTopic()
        {
            lock (_lock)
            {
                return _pendingEventTypes.FirstOrDefault();
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
                    observers = new List<object>();
                    _typeObservers[type] = observers;
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
                    observers.Remove(observer);
                    if (observers.Count == 0)
                    {
                        _typeObservers.Remove(type);
                    }
                }
            }
        }

        public void NotifyEvent<T>(T eventData)
        {
            List<IObserver<T>> observers;
            lock (_lock)
            {
                if (!_typeObservers.TryGetValue(typeof(T), out var observerList))
                    return;

                observers = observerList.Cast<IObserver<T>>().ToList();
            }

            foreach (var observer in observers)
            {
                observer.OnNext(eventData);
            }
        }
    }
}

