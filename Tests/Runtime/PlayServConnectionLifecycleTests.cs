using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.DataSubscription;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServConnectionLifecycleTests
    {
        [Test]
        public void TransportLifecycle_ReconnectsAfterReceiveStreamCompletes()
        {
            var transport = new TestTransportImplementation();
            var connectionLostCount = 0;
            using var lifecycle = new TransportConnectionLifecycle(
                transport,
                new TestLogger(),
                () => false,
                _ => { },
                () => connectionLostCount++);

            Assert.That(lifecycle.ConnectAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(transport.ReceiveSubscriptionCount, Is.EqualTo(1));

            transport.CompleteReceiveStream();
            Assert.That(connectionLostCount, Is.EqualTo(1));

            Assert.That(lifecycle.ConnectAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(transport.ConnectCount, Is.EqualTo(2));
            Assert.That(transport.ReceiveSubscriptionCount, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeSession_InitializesDataSubscriptionAfterCommandBusIsReady()
        {
            var transport = new TestTransportImplementation();

            Assert.DoesNotThrow(() =>
            {
                using var session = new PlayServImplementation(
                    "ws://example.test",
                    (_, __) => transport,
                    host => host.Register(new PlayServDataSubscriptionModule()));

                Assert.That(session.HasModule(PlayServModuleIds.Data), Is.True);
            });
        }

        private sealed class TestTransportImplementation : ITransportImplementation
        {
            private TestObservable _receiveStream;

            public int ConnectCount { get; private set; }

            public int ReceiveSubscriptionCount { get; private set; }

            public Task<bool> Connect()
            {
                ConnectCount++;
                return Task.FromResult(true);
            }

            public Task Send(byte[] data)
            {
                return Task.CompletedTask;
            }

            public void ResetConnection()
            {
            }

            public IObservable<byte[]> OnReceive()
            {
                _receiveStream = new TestObservable(() => ReceiveSubscriptionCount++);
                return _receiveStream;
            }

            public void CompleteReceiveStream()
            {
                _receiveStream?.Complete();
            }

            public void Dispose()
            {
            }
        }

        private sealed class TestObservable : IObservable<byte[]>
        {
            private readonly Action _onSubscribe;
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();

            public TestObservable(Action onSubscribe)
            {
                _onSubscribe = onSubscribe;
            }

            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                _onSubscribe();
                _observers.Add(observer);
                return new Subscription(_observers, observer);
            }

            public void Complete()
            {
                var observers = _observers.ToArray();
                _observers.Clear();
                foreach (var observer in observers)
                    observer.OnCompleted();
            }

            private sealed class Subscription : IDisposable
            {
                private readonly ICollection<IObserver<byte[]>> _observers;
                private readonly IObserver<byte[]> _observer;

                public Subscription(
                    ICollection<IObserver<byte[]>> observers,
                    IObserver<byte[]> observer)
                {
                    _observers = observers;
                    _observer = observer;
                }

                public void Dispose()
                {
                    _observers.Remove(_observer);
                }
            }
        }

        private sealed class TestLogger : ILogger
        {
            public void Log(string message)
            {
            }

            public void LogWarning(string message)
            {
            }

            public void LogError(string message)
            {
            }
        }
    }
}
