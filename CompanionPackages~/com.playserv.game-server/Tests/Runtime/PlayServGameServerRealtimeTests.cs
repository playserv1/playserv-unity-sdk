using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServGameServerRealtimeTests
    {
        private readonly List<HandshakeTransport> _transports = new List<HandshakeTransport>();

        [SetUp]
        public void SetUp()
        {
            _transports.Clear();
            PlayServGameServer.SetSupportedBuildForTesting(true);
            PlayServGameServer.CancelForModuleShutdown();
        }

        [TearDown]
        public void TearDown()
        {
            PlayServGameServer.Realtime.SetTransportFactoryForTesting(null);
            PlayServGameServer.CancelForModuleShutdown();
            PlayServGameServer.SetSupportedBuildForTesting(null);
        }

        [Test]
        public void Connect_UsesOnlyServerAuthorizationAndConfiguredMetadata()
        {
            Configure(new KeyProvider("sk_realtime"));
            InstallTransportFactory();

            var connected = PlayServGameServer.Realtime.ConnectAsync(
                    new PlayServGameServerRealtimeOptions
                    {
                        InstanceId = "instance_42",
                        GameVersion = "1.2.3"
                    })
                .GetAwaiter().GetResult();

            Assert.That(connected, Is.True);
            Assert.That(PlayServGameServer.Realtime.State, Is.EqualTo(PlayServState.Online));
            var handshake = _transports.Single().SentJson.Single(x => x.Contains("HandshakeRequest"));
            Assert.That(handshake, Does.Contain("\"GameAccessToken\":\"sk_realtime\""));
            Assert.That(handshake, Does.Contain("\"Authorization\":\"Bearer sk_realtime\""));
            Assert.That(handshake, Does.Not.Contain("\"GameId\""));
            Assert.That(handshake, Does.Not.Contain("\"UserId\""));
            Assert.That(handshake, Does.Not.Contain("pk_"));
        }

        [Test]
        public void ReconnectAfterExplicitDisconnect_ResolvesRotatedCredentialAgain()
        {
            var keys = new KeyProvider("sk_first", "sk_second");
            Configure(keys);
            InstallTransportFactory();

            Assert.That(PlayServGameServer.Realtime.ConnectAsync().GetAwaiter().GetResult(), Is.True);
            PlayServGameServer.Realtime.DisconnectAsync().GetAwaiter().GetResult();
            Assert.That(PlayServGameServer.Realtime.ConnectAsync().GetAwaiter().GetResult(), Is.True);

            Assert.That(keys.CallCount, Is.EqualTo(2));
            Assert.That(_transports[0].SentJson.Any(x => x.Contains("Bearer sk_first")), Is.True);
            Assert.That(_transports[1].SentJson.Any(x => x.Contains("Bearer sk_second")), Is.True);
        }

        [Test]
        public void RecordsSubscriptionBeforeConnect_ReturnsRetryableUnifiedFailure()
        {
            Configure(new KeyProvider("sk_catalogue"), new CatalogueHttpTransport());

            var exception = Assert.Throws<Playserv.DataSubscription.Exceptions.DataSubscriptionException>(() =>
                PlayServGameServer.Records<RealtimeRecord>()
                    .SubscribeAsync()
                    .GetAwaiter().GetResult());

            Assert.That(exception.UnifiedError.Retryable, Is.True);
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("subscription_connection_unavailable"));
        }

        private void InstallTransportFactory()
        {
            PlayServGameServer.Realtime.SetTransportFactoryForTesting((_, __) =>
            {
                var transport = new HandshakeTransport();
                _transports.Add(transport);
                return transport;
            });
        }

        private static void Configure(
            IPlayServServerKeyProvider provider,
            IPlayServGameServerTransport transport = null)
        {
            PlayServGameServer.ConfigureForTesting(
                new PlayServGameServerOptions
                {
                    BackendServerAddress = "https://api.playserv.test",
                    ServerKeyProvider = provider
                },
                transport ?? new UnusedHttpTransport(),
                () => DateTimeOffset.UtcNow,
                (_, __) => Task.CompletedTask);
        }

        private sealed class RealtimeRecord
        {
            public string Value;
        }

        private sealed class KeyProvider : IPlayServServerKeyProvider
        {
            private readonly Queue<string> _keys;
            private string _last;

            internal KeyProvider(params string[] keys)
            {
                _keys = new Queue<string>(keys);
            }

            public int CallCount { get; private set; }

            public Task<string> GetServerKeyAsync(System.Threading.CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                if (_keys.Count > 0)
                    _last = _keys.Dequeue();
                return Task.FromResult(_last);
            }
        }

        private sealed class HandshakeTransport : ITransportImplementation
        {
            private readonly ByteObservable _received = new ByteObservable();
            internal readonly List<string> SentJson = new List<string>();

            public Task<bool> Connect() => Task.FromResult(true);

            public Task Send(byte[] data)
            {
                var json = Encoding.UTF8.GetString(data);
                SentJson.Add(json);
                if (json.Contains("HandshakeRequest"))
                {
                    _received.Emit(Encoding.UTF8.GetBytes(
                        "{\"Command\":\"HandshakeResponse\",\"Payload\":{\"success\":true,\"errorCode\":0}}"));
                }
                return Task.CompletedTask;
            }

            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => _received;
            public void Dispose() => _received.Complete();
        }

        private sealed class ByteObservable : IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                _observers.Add(observer);
                return new Subscription(_observers, observer);
            }
            internal void Emit(byte[] value)
            {
                foreach (var observer in _observers.ToArray())
                    observer.OnNext(value);
            }
            internal void Complete()
            {
                foreach (var observer in _observers.ToArray())
                    observer.OnCompleted();
                _observers.Clear();
            }
            private sealed class Subscription : IDisposable
            {
                private readonly ICollection<IObserver<byte[]>> _observers;
                private readonly IObserver<byte[]> _observer;
                internal Subscription(ICollection<IObserver<byte[]>> observers, IObserver<byte[]> observer)
                {
                    _observers = observers;
                    _observer = observer;
                }
                public void Dispose() => _observers.Remove(_observer);
            }
        }

        private sealed class UnusedHttpTransport : IPlayServGameServerTransport
        {
            public Task<PlayServGameServerHttpResponse> SendAsync(
                PlayServGameServerHttpRequest request,
                System.Threading.CancellationToken cancellationToken) =>
                throw new AssertionException("HTTP transport should not be used by realtime tests.");
        }

        private sealed class CatalogueHttpTransport : IPlayServGameServerTransport
        {
            public Task<PlayServGameServerHttpResponse> SendAsync(
                PlayServGameServerHttpRequest request,
                System.Threading.CancellationToken cancellationToken)
            {
                Assert.That(request.RelativePath, Is.EqualTo("data/tables"));
                return Task.FromResult(new PlayServGameServerHttpResponse
                {
                    StatusCode = 200,
                    Body = "{\"data\":[{\"entity_id\":\"ent_realtime\",\"name\":\"RealtimeRecord\"," +
                           "\"singleton\":false,\"acl\":{\"server\":{\"read\":true,\"write\":true}}}]}"
                });
            }
        }
    }
}
