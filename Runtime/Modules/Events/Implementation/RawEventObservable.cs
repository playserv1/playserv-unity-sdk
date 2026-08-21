using System;
using Playserv.Events.Requests;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Events
{
    /// <summary>
    /// Observable that subscribes to an event topic but delivers the raw JSON payload string.
    /// </summary>
    internal sealed class RawEventObservable<TEvent> : IObservable<string>
    {
        private const string EventsModuleName = "module_events";

        private readonly ITransport _transport;
        private readonly EventSubscriptionManager _subscriptionManager;
        private readonly string _eventType;
        private readonly ILogger _logger;

        public RawEventObservable(ITransport transport, EventSubscriptionManager subscriptionManager, string eventType, ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
            _eventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable Subscribe(IObserver<string> observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            _subscriptionManager.AddRawObserver(_eventType, observer);
            EnsureSubscription();

            return new Unsubscriber(_subscriptionManager, _eventType, observer, TryUnsubscribe);
        }

        private void EnsureSubscription()
        {
            if (_subscriptionManager.IsSubscriptionPending(_eventType))
                return;

            if (_subscriptionManager.IsSubscribed(_eventType))
                return;

            _subscriptionManager.AddPendingSubscription(_eventType);
            var request = new EventSubscribeRequest(_eventType);
            _transport.Send(request, EventsModuleName);
            _logger.Log($"Sent raw subscription request for event type: {_eventType}");
        }

        private void TryUnsubscribe()
        {
            if (_subscriptionManager.HasObserversForEventType(_eventType))
                return;

            _subscriptionManager.RemoveSubscription(_eventType);
            _logger.Log($"Removed local raw event subscription for event type: {_eventType}");
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly EventSubscriptionManager _subscriptionManager;
            private readonly string _eventType;
            private readonly IObserver<string> _observer;
            private readonly Action _onDispose;
            private bool _disposed;

            public Unsubscriber(
                EventSubscriptionManager subscriptionManager,
                string eventType,
                IObserver<string> observer,
                Action onDispose)
            {
                _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
                _eventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
                _observer = observer ?? throw new ArgumentNullException(nameof(observer));
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _subscriptionManager.RemoveRawObserver(_eventType, _observer);
                _onDispose?.Invoke();
            }
        }
    }
}
