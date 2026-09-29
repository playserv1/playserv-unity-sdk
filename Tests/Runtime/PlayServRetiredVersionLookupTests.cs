using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Playserv.Proxy.Common;
using Playserv.Modules;
using Playserv.Proxy.Interfaces;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServRetiredVersionLookupTests
    {
        private string[] _selection;
        [SetUp] public void SelectCoreOnly()
        {
            _selection = PlayServModuleRegistry.GetRegisteredModuleIds().Where(PlayServModuleRegistry.IsSelected).ToArray();
            PlayServModuleRegistry.SetProjectSelection(Array.Empty<string>());
        }
        [TearDown] public void RestoreSelection() => PlayServModuleRegistry.SetProjectSelection(_selection);
        [UnityTest]
        public IEnumerator LegacyVersionLookupFailsWithoutSendingHttp() => CheckNoVersionRequest(false);

        [UnityTest]
        public IEnumerator ConnectKeepsConfiguredVersionWithoutSendingV1Http() => CheckNoVersionRequest(true);

        private IEnumerator CheckNoVersionRequest(bool connect)
        {
            var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            using var listener = new HttpListener();
            var endpoint = "http://127.0.0.1:" + port + "/";
            listener.Prefixes.Add(endpoint);
            listener.Start();
            var received = listener.GetContextAsync();
            var wireType = typeof(IPlayServRuntimeHttpClient).Assembly.GetType(
                "Playserv.Http.Modules.Unity.UnityWebRequestRuntimeHttpClient", true);
            var wire = (IPlayServRuntimeHttpClient)Activator.CreateInstance(wireType,
                new object[] { new PlayServRuntimeSettings { DeployApiServerAddress = endpoint }, null });
            var previous = TransportModuleRegistry.GetFactories().Single(f => f.Scheme == "wss");
            var transport = new HandshakeTransport();
            var api = new PlayServApi();
            Task request;
            TransportModuleRegistry.Register(transport);
            try
            {
                api.Config(new PlayServSettings { ClientToken = "pk_test", PlayerAccessToken = "fixture-jwt",
                    PlayerId = "plr_fixture", GameVersion = "configured-version", DeploymentGameId = "game-test",
                    BackendServerAddress = "wss://platform.test/ws", DeployApiServerAddress = endpoint,
                    ResolveLatestGameVersionOnConnect = true });
                request = connect ? (Task)api.Connect() : wire.GetLatestVersionAsync("game-test");
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!request.IsCompleted && !received.IsCompleted && DateTime.UtcNow < deadline)
                    yield return null;
                if (received.IsCompleted)
                {
                    var response = received.Result.Response;
                    var bytes = Encoding.UTF8.GetBytes("{\"version\":\"remote-version\"}");
                    response.ContentLength64 = bytes.Length;
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    response.Close();
                    while (!request.IsCompleted && DateTime.UtcNow < deadline) yield return null;
                }
                Assert.That(received.IsCompleted, Is.False, "Retired V1 lookup sent an HTTP request.");
                Assert.That(request.IsCompleted, Is.True);
                if (connect)
                {
                    Assert.That(((Task<bool>)request).GetAwaiter().GetResult(), Is.True);
                    Assert.That(api.State, Is.EqualTo(PlayServState.Online));
                    Assert.That(api.Settings.GameVersion, Is.EqualTo("configured-version"));
                    Assert.That(transport.Handshake.ToString(), Does.Contain("configured-version"));
                }
                else
                {
                    var error = Assert.Throws<NotSupportedException>(() => request.GetAwaiter().GetResult());
                    Assert.That(error.Message, Does.Contain("GameVersion"));
                }
            }
            finally { api.Disconnect(); TransportModuleRegistry.Register(previous); }
        }

        [Test]
        public void PublicVersionLookupDoesNotReturnTheConfiguredVersionAsAFalseSuccess()
        {
            var api = new PlayServApi();
            try
            {
                api.Config(new PlayServSettings { ClientToken = "pk_test", GameVersion = "configured-version", BackendServerAddress = "wss://platform.test/ws" });
                var error = Assert.Throws<NotSupportedException>(() => api.GetLatestVersionAsync("game-test").GetAwaiter().GetResult());
                Assert.That(error.Message, Does.Contain("GameVersion"));
            }
            finally { api.Disconnect(); }
        }
        private sealed class HandshakeTransport : ITransportModuleFactory, ITransportImplementation, IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
            internal JToken Handshake;
            public string Scheme => "wss";
            public ITransportImplementation Create(TransportModuleContext context) => this;
            public Task<bool> Connect() => Task.FromResult(true);
            public Task Send(byte[] bytes)
            {
                var frame = JObject.Parse(Encoding.UTF8.GetString(bytes));
                if (((string)frame["Command"])?.EndsWith("HandshakeRequest", StringComparison.Ordinal) == true)
                {
                    Handshake = frame["Payload"];
                    var reply = Encoding.UTF8.GetBytes("{\"Command\":\"HandshakeResponse\",\"Payload\":{\"success\":true,\"errorCode\":0}}");
                    foreach (var observer in _observers.ToArray()) observer.OnNext(reply);
                }
                return Task.CompletedTask;
            }
            public void ResetConnection() => _observers.Clear();
            public void Dispose() => _observers.Clear();
            public IObservable<byte[]> OnReceive() => this;
            public IDisposable Subscribe(IObserver<byte[]> observer) { _observers.Add(observer); return new Subscription(() => _observers.Remove(observer)); }
            private sealed class Subscription : IDisposable
            {
                private readonly Action _dispose;
                internal Subscription(Action dispose) => _dispose = dispose;
                public void Dispose() => _dispose();
            }
        }

    }
}
