using System;
using System.Collections;
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
using Playserv.Wrapper;
using UnityEngine.TestTools;

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

        [UnityTest]
        public IEnumerator GroupRejection_PreservesBackendCode_AndIsNotReplayed()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);

            var subscribe = adapter.SubscribeGroupAsync("project:other:room");
            transport.Emit(new SubscribeGroupResponse
            {
                GroupName = "project:other:room",
                success = false,
                errorCode = (int)TransportErrorCode.GroupOutsideProject,
                errorMessage = "That group belongs to another project."
            });

            for (var frame = 0; frame < 60 && !subscribe.IsCompleted; frame++)
                yield return null;

            Assert.That(subscribe.IsCompleted, Is.True);
            PlayServGroupSubscriptionException exception = null;
            try
            {
                subscribe.GetAwaiter().GetResult();
            }
            catch (PlayServGroupSubscriptionException caught)
            {
                exception = caught;
            }

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.GroupName, Is.EqualTo("project:other:room"));
            Assert.That(exception.Operation, Is.EqualTo("subscribe"));
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Forbidden));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("group_outside_project"));
            Assert.That(exception.UnifiedError.TransportCode, Is.EqualTo(2005));

            transport.Sent.Clear();
            adapter.ResubscribeAllOnReconnect();
            Assert.That(transport.Sent.OfType<SubscribeGroupRequest>(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator GroupAcknowledgement_IsCorrelatedByGroupName()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);

            var subscribe = adapter.SubscribeGroupAsync("room-a");
            transport.Emit(new SubscribeGroupResponse { GroupName = "room-b", success = true });
            Assert.That(subscribe.IsCompleted, Is.False);
            transport.Emit(new SubscribeGroupResponse { GroupName = "room-a", success = true });

            for (var frame = 0; frame < 60 && !subscribe.IsCompleted; frame++)
                yield return null;

            Assert.That(subscribe.IsCompleted, Is.True);
            Assert.That(subscribe.GetAwaiter().GetResult(), Is.True);
        }

        [UnityTest]
        public IEnumerator GroupLimitRejection_IsCorrelatedAndOnlyAcceptedGroupReplays()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);
            var allowed = adapter.SubscribeGroupAsync("room-ok");
            var rejected = adapter.SubscribeGroupAsync("room-limit");
            transport.Emit(new SubscribeGroupResponse
            {
                GroupName = "room-limit", success = false,
                errorCode = (int)TransportErrorCode.GroupSubscriptionLimitReached,
                errorMessage = "Configured group limit exceeded."
            });
            Assert.That(allowed.IsCompleted, Is.False);
            transport.Emit(new SubscribeGroupResponse { GroupName = "room-ok", success = true });
            // Group commands are serialized. A response received before the second
            // request exists is stray; wait for that request before replying to it.
            var deadline = UnityEngine.Time.realtimeSinceStartup + 5;
            while (!transport.Sent.OfType<SubscribeGroupRequest>().Any(request => request.GroupName == "room-limit") &&
                   UnityEngine.Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(transport.Sent.OfType<SubscribeGroupRequest>().Any(request => request.GroupName == "room-limit"), Is.True);
            transport.Emit(new SubscribeGroupResponse
            {
                GroupName = "room-limit", success = false,
                errorCode = (int)TransportErrorCode.GroupSubscriptionLimitReached,
                errorMessage = "Configured group limit exceeded."
            });
            while ((!allowed.IsCompleted || !rejected.IsCompleted) && UnityEngine.Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(allowed.IsCompleted && rejected.IsCompleted, Is.True);
            Assert.That(allowed.GetAwaiter().GetResult(), Is.True);
            var error = Assert.Throws<PlayServGroupSubscriptionException>(() => rejected.GetAwaiter().GetResult());
            Assert.That(error.GroupName, Is.EqualTo("room-limit"));
            Assert.That(error.Operation, Is.EqualTo("subscribe"));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("group_subscription_limit_reached"));
            Assert.That(error.UnifiedError.TransportCode, Is.EqualTo(2006));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Transport));
            Assert.That(error.UnifiedError.Retryable, Is.False);
            Assert.That(error.Message, Is.EqualTo("Configured group limit exceeded."));
            transport.Sent.Clear();
            adapter.ResubscribeAllOnReconnect();
            Assert.That(transport.Sent.OfType<SubscribeGroupRequest>().Select(request => request.GroupName), Is.EqualTo(new[] { "room-ok" }));
        }

        [Test]
        public void GroupLimitTransportError_UsesStableSourceAndRequiresCallerAction()
        {
            var error = TransportError.FromCode(TransportErrorCode.GroupSubscriptionLimitReached);
            Assert.That(error.UnifiedError.TransportCode, Is.EqualTo(2006));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("group_subscription_limit_reached"));
            Assert.That(error.UnifiedError.Retryable, Is.False);
            Assert.That(error.ToString(), Does.StartWith("[02006]"));
        }

        [Test]
        public void NewAdmissionErrors_MapToStableUnifiedErrors()
        {
            var credential = TransportError.FromCode(TransportErrorCode.PlayerCredentialRequired);
            Assert.That(credential.UnifiedError.TransportCode, Is.EqualTo(2004));
            Assert.That(credential.UnifiedError.SourceCode, Is.EqualTo(nameof(TransportErrorCode.PlayerCredentialRequired)));

            var group = TransportError.FromCode(TransportErrorCode.GroupOutsideProject);
            Assert.That(group.UnifiedError.TransportCode, Is.EqualTo(2005));
            Assert.That(group.Message, Does.Contain("another project"));
        }

        [Test]
        public void RuntimeEventPublishingIsRejectedBeforeTransportIo()
        {
            var transport = new EventTransport();
            var adapter = CreateAdapter(transport);

            var broadcast = Assert.Throws<PlayServEventPublishingException>(() =>
                adapter.Publish(new FirstEvent()));
            var group = Assert.Throws<PlayServEventPublishingException>(() =>
                adapter.PublishForGroup("room-a", new FirstEvent()));
            var user = Assert.Throws<PlayServEventPublishingException>(() =>
                adapter.PublishForUser("plr_target", new FirstEvent()));

            Assert.That(broadcast.Target, Is.EqualTo("broadcast"));
            Assert.That(group.Target, Is.EqualTo("group:room-a"));
            Assert.That(user.Target, Is.EqualTo("user:plr_target"));
            Assert.That(broadcast.UnifiedError.SourceCode,
                Is.EqualTo("event_publishing_not_supported"));
            Assert.That(broadcast.UnifiedError.Retryable, Is.False);
            Assert.That(transport.Sent, Is.Empty);
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
            private readonly TestObservable<SubscribeGroupResponse> _groupSubscribeResponses =
                new TestObservable<SubscribeGroupResponse>();

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
                if (typeof(T) == typeof(SubscribeGroupResponse))
                    return (IObservable<T>)(object)_groupSubscribeResponses;
                return new TestObservable<T>();
            }
            public IObservable<object> OnReceive(string commandName) => new TestObservable<object>();
            public void Emit(EventSubscribeResponse response) => _subscribeResponses.Emit(response);
            public void Emit(SubscribeGroupResponse response) => _groupSubscribeResponses.Emit(response);
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
