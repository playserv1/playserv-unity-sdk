using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Proxy.Common;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.Tests.Runtime
{
    public sealed class TransportCredentialLoggingTests
    {
        private const string ClientToken = "pk_transport_logging_secret";
        private const string ServerKey = "sk_transport_logging_secret";
        private const string PlayerJwt = "a.b.c";
        private const string OpaqueCredential = "opaque-transport-credential";

        [Test]
        public void Sender_RedactsHandshakeAndRefreshCredentialsWithoutChangingWireBytes()
        {
            var fixture = new TransportFixture();

            fixture.RuntimeTransport.Send(new HandshakeRequest
            {
                GameAccessToken = ClientToken,
                ClientToken = ClientToken,
                Authorization = "Bearer " + PlayerJwt,
                SdkVersion = "0.5.1",
                GameVersion = "game-42"
            }).GetAwaiter().GetResult();
            fixture.RuntimeTransport.Send(new HandshakeRequest
            {
                GameAccessToken = ServerKey,
                Authorization = "Bearer " + ServerKey,
                SdkVersion = "0.5.1",
                GameVersion = "server-42"
            }).GetAwaiter().GetResult();
            fixture.RuntimeTransport.Send(new RefreshAuthRequest
            {
                Authorization = "Bearer " + PlayerJwt
            }).GetAwaiter().GetResult();

            var wire = string.Join("\n", fixture.Implementation.Sent.Select(data => Encoding.UTF8.GetString(data)));
            Assert.That(wire, Does.Contain(ClientToken));
            Assert.That(wire, Does.Contain(ServerKey));
            Assert.That(wire, Does.Contain(PlayerJwt));

            AssertNoCredentialReachedLogger(fixture.Logger);
            Assert.That(fixture.Logger.AllText, Does.Contain("HandshakeRequest"));
            Assert.That(fixture.Logger.AllText, Does.Contain("game-42"));
            Assert.That(fixture.Logger.AllText, Does.Contain("server-42"));
            Assert.That(fixture.Logger.AllText, Does.Contain("0.5.1"));
            Assert.That(fixture.Logger.AllText, Does.Contain("[REDACTED]"));
        }

        [Test]
        public void Receiver_RedactsCredentialsFromFramesAndDeserializationFailures()
        {
            var fixture = new TransportFixture();
            fixture.Observe<HandshakeRequest>();

            fixture.Implementation.Emit(fixture.SerializeFrame(new HandshakeRequest
            {
                GameAccessToken = ClientToken,
                ClientToken = ClientToken,
                Authorization = "Bearer " + PlayerJwt,
                SdkVersion = "0.5.1",
                GameVersion = "game-42"
            }));
            fixture.Implementation.Emit(fixture.SerializeFrame(new HandshakeRequest
            {
                GameAccessToken = ServerKey,
                Authorization = "Bearer " + ServerKey,
                SdkVersion = "0.5.1",
                GameVersion = "server-42"
            }));
            fixture.Implementation.Emit(fixture.SerializeFrame(new RefreshAuthRequest
            {
                Authorization = "Bearer " + PlayerJwt
            }));

            var stringifiedPayload = fixture.JsonCodec.Serialize(new Dictionary<string, object>
            {
                ["metadata"] = fixture.JsonCodec.Serialize(new Dictionary<string, object>
                {
                    ["client_token"] = ClientToken,
                    ["game-access-token"] = OpaqueCredential,
                    ["futureCredential"] = "credential=" + ServerKey,
                    ["futureJwt"] = PlayerJwt,
                    ["version"] = "1.2.3",
                    ["nested"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["AUTHORIZATION"] = "Bearer " + ServerKey
                        }
                    }
                })
            });
            fixture.Implementation.Emit(fixture.SerializeEnvelope(
                new MessageEnvelope("UnknownCredentialFrame", stringifiedPayload)));
            fixture.Implementation.Emit(fixture.SerializeEnvelope(
                new MessageEnvelope(ServerKey, "{}")));

            AssertNoCredentialReachedLogger(fixture.Logger);
            Assert.That(fixture.Logger.AllText, Does.Contain("Received JSON"));
            Assert.That(fixture.Logger.AllText, Does.Contain("UnknownCredentialFrame"));
            Assert.That(fixture.Logger.AllText, Does.Contain("1.2.3"));
            Assert.That(fixture.Logger.AllText, Does.Contain("[REDACTED]"));
            Assert.That(fixture.Logger.Errors, Is.Not.Empty);
        }

        [Test]
        public void Sender_RedactsCredentialShapesFromTransportErrorMessages()
        {
            var fixture = new TransportFixture();
            fixture.Implementation.SendException = new InvalidOperationException(
                "send failed with Authorization: Bearer " + OpaqueCredential +
                ", server key " + ServerKey + " and token " + PlayerJwt);

            Assert.Throws<InvalidOperationException>(() =>
                fixture.RuntimeTransport.Send(new RefreshAuthRequest
                {
                    Authorization = "Bearer " + PlayerJwt
                }).GetAwaiter().GetResult());

            AssertNoCredentialReachedLogger(fixture.Logger);
            Assert.That(fixture.Logger.Errors, Is.Not.Empty);
            Assert.That(fixture.Logger.AllText, Does.Contain("[REDACTED]"));
        }

        [Test]
        public void Receiver_FailsClosedWhenMalformedJsonContainsCredentials()
        {
            var fixture = new TransportFixture();
            fixture.Observe<HandshakeRequest>();
            var malformed = Encoding.UTF8.GetBytes(
                "{\"Command\":\"Broken\",\"Payload\":{\"Authorization\":\"Bearer " + ServerKey + "\"");

            fixture.Implementation.Emit(malformed);

            AssertNoCredentialReachedLogger(fixture.Logger);
            Assert.That(fixture.Logger.Errors, Is.Not.Empty);
            Assert.That(fixture.Logger.AllText, Does.Contain("[REDACTED]"));
        }

        [Test]
        public void Receiver_RedactsCredentialsFromSubscriberExceptions()
        {
            var fixture = new TransportFixture();
            fixture.Observe(new ThrowingObserver<HandshakeRequest>(
                new InvalidOperationException(
                    "subscriber failed for Bearer " + PlayerJwt + " using " + ServerKey)));

            fixture.Implementation.Emit(fixture.SerializeFrame(new HandshakeRequest
            {
                GameAccessToken = ClientToken,
                ClientToken = ClientToken,
                Authorization = "Bearer " + PlayerJwt,
                SdkVersion = "0.5.1",
                GameVersion = "game-42"
            }));

            AssertNoCredentialReachedLogger(fixture.Logger);
            Assert.That(fixture.Logger.Errors, Is.Not.Empty);
            Assert.That(fixture.Logger.AllText, Does.Contain("[REDACTED]"));
        }

        [Test]
        public void Receiver_FailsClosedForStringifiedJsonAtMaximumDepth()
        {
            var fixture = new TransportFixture();
            fixture.Observe<HandshakeRequest>();
            object nestedValue = fixture.JsonCodec.Serialize(new Dictionary<string, object>
            {
                ["Authorization"] = OpaqueCredential
            });

            for (var depth = 0; depth < 15; depth++)
            {
                nestedValue = new Dictionary<string, object>
                {
                    ["level"] = nestedValue
                };
            }

            fixture.Implementation.Emit(fixture.SerializeEnvelope(
                new MessageEnvelope("UnknownDeepFrame", fixture.JsonCodec.Serialize(nestedValue))));

            AssertNoCredentialReachedLogger(fixture.Logger);
            Assert.That(fixture.Logger.AllText, Does.Contain("[REDACTED]"));
        }

        private static void AssertNoCredentialReachedLogger(RecordingLogger logger)
        {
            Assert.That(logger.AllText, Does.Not.Contain(ClientToken));
            Assert.That(logger.AllText, Does.Not.Contain(ServerKey));
            Assert.That(logger.AllText, Does.Not.Contain(PlayerJwt));
            Assert.That(logger.AllText, Does.Not.Contain(OpaqueCredential));
        }

        private sealed class TransportFixture
        {
            public TransportFixture()
            {
                JsonCodec = new NewtonsoftJsonCodec();
                Serializer = new Playserv.Proxy.Implementation.JsonSerializer(
                    JsonCodec,
                    new NewtonsoftCommandPayloadMapper());
                Logger = new RecordingLogger();
                Implementation = new RecordingTransportImplementation();
                RuntimeTransport = new Transport(
                    Implementation,
                    Serializer,
                    new RequestIdGenerator(),
                    Logger);
            }

            public IJsonCodec JsonCodec { get; }

            public Playserv.Proxy.Implementation.JsonSerializer Serializer { get; }

            public RecordingLogger Logger { get; }

            public RecordingTransportImplementation Implementation { get; }

            public Transport RuntimeTransport { get; }

            public void Observe<T>()
            {
                RuntimeTransport.OnReceive<T>().Subscribe(new EmptyObserver<T>());
            }

            public void Observe<T>(IObserver<T> observer)
            {
                RuntimeTransport.OnReceive<T>().Subscribe(observer);
            }

            public byte[] SerializeFrame<T>(T command)
            {
                return SerializeEnvelope(Serializer.Serialize(command));
            }

            public byte[] SerializeEnvelope(MessageEnvelope envelope)
            {
                var payload = string.IsNullOrWhiteSpace(envelope.Payload)
                    ? null
                    : JsonCodec.ParseToPlainValue(envelope.Payload);
                var json = JsonCodec.Serialize(new Dictionary<string, object>
                {
                    ["Command"] = envelope.Command,
                    ["Payload"] = payload
                });
                return Encoding.UTF8.GetBytes(json);
            }
        }

        private sealed class RecordingTransportImplementation : ITransportImplementation
        {
            private readonly TestObservable _receive = new TestObservable();

            public List<byte[]> Sent { get; } = new List<byte[]>();

            public Exception SendException { get; set; }

            public Task<bool> Connect()
            {
                return Task.FromResult(true);
            }

            public Task Send(byte[] data)
            {
                if (SendException != null)
                    throw SendException;

                Sent.Add(data);
                return Task.CompletedTask;
            }

            public void ResetConnection()
            {
            }

            public IObservable<byte[]> OnReceive()
            {
                return _receive;
            }

            public void Emit(byte[] data)
            {
                _receive.Emit(data);
            }

            public void Dispose()
            {
            }
        }

        private sealed class TestObservable : IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();

            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                _observers.Add(observer);
                return new Subscription(_observers, observer);
            }

            public void Emit(byte[] data)
            {
                foreach (var observer in _observers.ToArray())
                    observer.OnNext(data);
            }
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

        private sealed class EmptyObserver<T> : IObserver<T>
        {
            public void OnNext(T value)
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }

        private sealed class ThrowingObserver<T> : IObserver<T>
        {
            private readonly Exception _exception;

            public ThrowingObserver(Exception exception)
            {
                _exception = exception;
            }

            public void OnNext(T value)
            {
                throw _exception;
            }

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }

        private sealed class RecordingLogger : ILogger
        {
            public List<string> Messages { get; } = new List<string>();

            public List<string> Warnings { get; } = new List<string>();

            public List<string> Errors { get; } = new List<string>();

            public string AllText => string.Join("\n", Messages.Concat(Warnings).Concat(Errors));

            public void Log(string message)
            {
                Messages.Add(message ?? string.Empty);
            }

            public void LogWarning(string message)
            {
                Warnings.Add(message ?? string.Empty);
            }

            public void LogError(string message)
            {
                Errors.Add(message ?? string.Empty);
            }
        }
    }
}
