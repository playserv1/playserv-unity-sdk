using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServServiceRestartConnectionTests
    {
        [UnityTest]
        public IEnumerator ServiceRestart1012_reconnects_player_and_repeats_handshake_without_manual_Connect()
        {
            Assert.That(Application.isPlaying, Is.True, "The SDK reconnects only while the player is running.");
            var task = ReconnectPlayer();
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Player reconnect did not complete.");
            task.GetAwaiter().GetResult();
        }

        private static async Task ReconnectPlayer()
        {
            var wire = new RestartTransport();
            using var session = new PlayServImplementation("wss://platform.test/ws", (_, __) => wire);
            var errors = new List<TransportError>();
            session.OnTransportError += errors.Add;
            session.SetConfig("pk_restart_test", "plr_restart_test", "1", playerAccessToken: "player-test-token");
            Assert.That(await session.Connect(), Is.True);
            Assert.That(session.State, Is.EqualTo(PlayServState.Online));

            for (var restart = 1; restart <= 2; restart++)
            {
                wire.RemoteClose(1012, "service_restart");
                Assert.That(session.State, Is.EqualTo(PlayServState.Reconnecting));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (session.State == PlayServState.Reconnecting && DateTime.UtcNow < deadline)
                    await Task.Yield();
                Assert.That(session.State, Is.EqualTo(PlayServState.Online));
                Assert.That(wire.ConnectCount, Is.EqualTo(restart + 1));
                Assert.That(wire.Handshakes.Count, Is.EqualTo(restart + 1));
                Assert.That(session.PlayerId, Is.EqualTo("plr_restart_test"));
                Assert.That(errors, Is.Empty);
            }
            Assert.That(wire.Handshakes.All(json => JToken.DeepEquals(json, wire.Handshakes[0])), Is.True,
                "Each recovered connection must repeat the configured player handshake.");
        }

        private sealed class RestartTransport : ITransportImplementation, IPlayServTransportCloseInfoSource, IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
            internal readonly List<JToken> Handshakes = new List<JToken>();
            internal int ConnectCount;
            public event Action<PlayServTransportCloseInfo> Closed;
            public Task<bool> Connect() { ConnectCount++; return Task.FromResult(true); }
            public Task Send(byte[] bytes)
            {
                var frame = JObject.Parse(Encoding.UTF8.GetString(bytes));
                if (((string)frame["Command"])?.EndsWith("HandshakeRequest", StringComparison.Ordinal) == true)
                {
                    Handshakes.Add(frame["Payload"].DeepClone());
                    var response = Encoding.UTF8.GetBytes("{\"Command\":\"HandshakeResponse\",\"Payload\":{\"success\":true,\"errorCode\":0}}");
                    foreach (var observer in _observers.ToArray()) observer.OnNext(response);
                }
                return Task.CompletedTask;
            }
            internal void RemoteClose(int code, string reason)
            {
                Closed?.Invoke(new PlayServTransportCloseInfo(code, reason));
                foreach (var observer in _observers.ToArray()) observer.OnCompleted();
                _observers.Clear();
            }
            public void ResetConnection() => _observers.Clear();
            public IObservable<byte[]> OnReceive() => this;
            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                _observers.Add(observer);
                return new Subscription(() => _observers.Remove(observer));
            }
            public void Dispose() => _observers.Clear();
            private sealed class Subscription : IDisposable
            {
                private readonly Action _dispose;
                internal Subscription(Action dispose) => _dispose = dispose;
                public void Dispose() => _dispose();
            }
        }
    }
}
