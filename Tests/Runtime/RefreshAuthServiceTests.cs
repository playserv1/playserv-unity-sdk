using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Common;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;

namespace Playserv.Tests.Runtime
{
    public sealed class RefreshAuthServiceTests
    {
        [TestCase("player.jwt.value")]
        [TestCase("Bearer player.jwt.value")]
        public void RefreshAsync_SendsNormalizedBearerCredential(string token)
        {
            var transport = new TestTransport(new RefreshAuthResponse { success = true });
            using var service = new RefreshAuthService(transport, new TestLogger());

            var success = service.RefreshAsync(token).GetAwaiter().GetResult();

            Assert.That(success, Is.True);
            Assert.That(transport.LastRequest, Is.Not.Null);
            Assert.That(transport.LastRequest.Authorization, Is.EqualTo("Bearer player.jwt.value"));
        }

        [Test]
        public void RefreshAsync_ReturnsFalseWhenBackendRejectsCredential()
        {
            var transport = new TestTransport(new RefreshAuthResponse
            {
                success = false,
                message = "expired"
            });
            using var service = new RefreshAuthService(transport, new TestLogger());

            var success = service.RefreshAsync("player.jwt.value").GetAwaiter().GetResult();

            Assert.That(success, Is.False);
        }

        [Test]
        public void RefreshAsync_RejectsPublicClientKeyAsPlayerCredential()
        {
            var transport = new TestTransport(new RefreshAuthResponse { success = true });
            using var service = new RefreshAuthService(transport, new TestLogger());

            Assert.Throws<InvalidOperationException>(
                () => service.RefreshAsync("pk_public").GetAwaiter().GetResult());
        }

        [TestCase("sk_secret")]
        [TestCase("player.jwt.value")]
        public void AnonymousSignIn_RejectsNonPublicClientCredential(string token)
        {
            var client = PlayServRuntimeHttpClientResolver.Create(
                new PlayServHttpModuleContext(new PlayServRuntimeSettings
                {
                    BackendServerAddress = "wss://backend.example.test/ws"
                }));

            Assert.Throws<InvalidOperationException>(
                () => client.SignInAnonAsync(token).GetAwaiter().GetResult());
        }

        private sealed class TestTransport : ITransport
        {
            private readonly TestObservable<RefreshAuthResponse> _responses =
                new TestObservable<RefreshAuthResponse>();
            private readonly RefreshAuthResponse _response;

            public TestTransport(RefreshAuthResponse response)
            {
                _response = response;
            }

            public event EventHandler ConnectionLost
            {
                add { }
                remove { }
            }

            public RefreshAuthRequest LastRequest { get; private set; }

            public Task<bool> Connect() => Task.FromResult(true);

            public Task Send<T>(T command, string moduleName = null)
            {
                LastRequest = command as RefreshAuthRequest;
                if (LastRequest != null)
                    _responses.Emit(_response);

                return Task.CompletedTask;
            }

            public void ResetConnection()
            {
            }

            public IObservable<T> OnReceive<T>()
            {
                if (typeof(T) == typeof(RefreshAuthResponse))
                    return (IObservable<T>)(object)_responses;

                return new TestObservable<T>();
            }

            public IObservable<object> OnReceive(string commandName) => new TestObservable<object>();

            public void Dispose()
            {
            }
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
                var observers = _observers.ToArray();
                for (var i = 0; i < observers.Length; i++)
                    observers[i].OnNext(value);
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
