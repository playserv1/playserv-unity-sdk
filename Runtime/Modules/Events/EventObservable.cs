#if !PLAYSERV_DISABLE_EVENTS
using System;
using Playserv.Events.Requests;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Events
{
    internal sealed class EventObservable<T> : IObservable<T>
    {
        private readonly ITransport _transport;
        private readonly EventSubscriptionManager _subscriptionManager;
        private readonly string _eventType;
        private readonly ILogger _logger;

        public EventObservable(ITransport transport, EventSubscriptionManager subscriptionManager, string eventType, ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
            _eventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            EnsureSubscription();
            _subscriptionManager.AddObserver(observer);

            return new Unsubscriber(_subscriptionManager, observer, TryUnsubscribe);
        }

        private void EnsureSubscription()
        {
            if (_subscriptionManager.IsSubscriptionPending(_eventType))
                return;

            if (_subscriptionManager.GetSubscriptionId(_eventType) != null)
                return;

            _subscriptionManager.AddPendingSubscription(_eventType);
            var request = new EventSubscribeRequest(_eventType);
            _transport.Send(request);
            _logger.Log($"Sent subscription request for event type: {_eventType}");
        }

        private void TryUnsubscribe()
        {
            if (_subscriptionManager.HasObservers<T>())
                return;

            var subscriptionId = _subscriptionManager.GetSubscriptionId(_eventType);
            if (subscriptionId == null)
                return;

            var request = new EventUnsubscribeRequest(subscriptionId);
            _transport.Send(request);
            _subscriptionManager.RemoveSubscription(_eventType);
            _logger.Log($"Sent unsubscription request for event type: {_eventType}");
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly EventSubscriptionManager _subscriptionManager;
            private readonly IObserver<T> _observer;
            private readonly Action _onDispose;
            private bool _disposed;

            public Unsubscriber(EventSubscriptionManager subscriptionManager, IObserver<T> observer, Action onDispose)
            {
                _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
                _observer = observer ?? throw new ArgumentNullException(nameof(observer));
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _subscriptionManager.RemoveObserver(_observer);
                _onDispose?.Invoke();
            }
        }
    }
}
#endif
