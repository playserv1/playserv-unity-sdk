using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Events;
using Playserv.Events.Requests;
using Playserv.Events.Responses;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServEventSubscriptionProtocolTests
    {
        [Test]
        public void ConcurrentTopics_AreCorrelatedByEventTypeOutOfOrder()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);
            var first = new RecordingObserver<FirstEvent>();
            var second = new RecordingObserver<SecondEvent>();

            using (adapter.Subscribe<FirstEvent>().Subscribe(first))
            using (adapter.Subscribe<SecondEvent>().Subscribe(second))
            {
                Assert.That(transport.SubscribeRequests.Select(x => x.EventType),
                    Is.EquivalentTo(new[] { FirstEventType, SecondEventType }));

                transport.Emit(new EventSubscribeResponse
                {
                    EventType = SecondEventType,
                    success = true
                });
                transport.Emit(new EventSubscribeResponse
                {
                    EventType = FirstEventType,
                    success = true
                });

                Assert.That(first.Error, Is.Null);
                Assert.That(second.Error, Is.Null);
            }
        }

        [Test]
        public void DuplicateObservers_ShareOneSubscribe_AndLastDisposeIsLocalOnly()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);
            var observable = adapter.Subscribe<FirstEvent>();
            var first = observable.Subscribe(new RecordingObserver<FirstEvent>());
            var second = observable.Subscribe(new RecordingObserver<FirstEvent>());

            Assert.That(transport.SubscribeRequests, Has.Count.EqualTo(1));
            transport.Emit(new EventSubscribeResponse { EventType = FirstEventType, success = true });

            first.Dispose();
            second.Dispose();

            Assert.That(transport.Sent.Any(x => x is EventUnsubscribeRequest), Is.False);
        }

        [Test]
        public void Rejection_FailsOnlyMatchingTopic_WithUnifiedError()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);
            var first = new RecordingObserver<FirstEvent>();
            var second = new RecordingObserver<SecondEvent>();
            adapter.Subscribe<FirstEvent>().Subscribe(first);
            adapter.Subscribe<SecondEvent>().Subscribe(second);

            transport.Emit(new EventSubscribeResponse
            {
                EventType = FirstEventType,
                success = false,
                errorCode = 42001,
                errorMessage = "topic rejected"
            });

            var exception = first.Error as PlayServEventSubscriptionException;
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.EventType, Is.EqualTo(FirstEventType));
            Assert.That(exception.UnifiedError.TransportCode, Is.EqualTo(42001));
            Assert.That(second.Error, Is.Null);
        }

        [Test]
        public void Reconnect_ReplaysOneSubscribePerObservedEventType()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);
            adapter.Subscribe<FirstEvent>().Subscribe(new RecordingObserver<FirstEvent>());
            adapter.Subscribe<FirstEvent>().Subscribe(new RecordingObserver<FirstEvent>());
            transport.Emit(new EventSubscribeResponse { EventType = FirstEventType, success = true });

            transport.Sent.Clear();
            adapter.ResubscribeAllOnReconnect();

            Assert.That(transport.SubscribeRequests, Has.Count.EqualTo(1));
            Assert.That(transport.SubscribeRequests[0].EventType, Is.EqualTo(FirstEventType));
        }

        private static PlayServEventsAdapter CreateAdapter(EventTransport transport) =>
            new PlayServEventsAdapter(transport, new NewtonsoftJsonCodec(), new SilentLogger());

        private static string FirstEventType => typeof(FirstEvent).FullName;
        private static string SecondEventType => typeof(SecondEvent).FullName;

        private sealed class FirstEvent { }
        private sealed class SecondEvent { }

        private sealed class RecordingObserver<T> : IObserver<T>
        {
            public Exception Error { get; private set; }
            public void OnCompleted() { }
            public void OnError(Exception error) => Error = error;
            public void OnNext(T value) { }
        }

        private sealed class EventTransport : ITransport
        {
            private readonly TestObservable<EventSubscribeResponse> _subscribeResponses =
                new TestObservable<EventSubscribeResponse>();

            public readonly List<object> Sent = new List<object>();
            public List<EventSubscribeRequest> SubscribeRequests => Sent.OfType<EventSubscribeRequest>().ToList();
            public event EventHandler ConnectionLost { add { } remove { } }
            public Task<bool> Connect() => Task.FromResult(true);
            public Task Send<T>(T command, string moduleName = null)
            {
                Sent.Add(command);
                return Task.CompletedTask;
            }
            public void ResetConnection() { }
            public IObservable<T> OnReceive<T>()
            {
                if (typeof(T) == typeof(EventSubscribeResponse))
                    return (IObservable<T>)(object)_subscribeResponses;
                return new TestObservable<T>();
            }
            public IObservable<object> OnReceive(string commandName) => new TestObservable<object>();
            public void Emit(EventSubscribeResponse response) => _subscribeResponses.Emit(response);
            public void Dispose() { }
        }

        private sealed class TestObservable<T> : IObservable<T>
        {
            private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();
            public IDisposable Subscribe(IObserver<T> observer)
            {
                _observers.Add(observer);
                return new Subscription(_observers, observer);
            }
            public void Emit(T value)
            {
                foreach (var observer in _observers.ToArray())
                    observer.OnNext(value);
            }
            private sealed class Subscription : IDisposable
            {
                private readonly ICollection<IObserver<T>> _observers;
                private readonly IObserver<T> _observer;
                public Subscription(ICollection<IObserver<T>> observers, IObserver<T> observer)
                {
                    _observers = observers;
                    _observer = observer;
                }
                public void Dispose() => _observers.Remove(_observer);
            }
        }

        private sealed class SilentLogger : ILogger
        {
            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
    }
}
