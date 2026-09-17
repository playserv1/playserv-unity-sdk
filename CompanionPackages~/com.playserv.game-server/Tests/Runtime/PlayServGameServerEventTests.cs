using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServGameServerEventTests
    {
        private ConcurrentQueue<H.Socket> _sockets;
        [SetUp] public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown(); _sockets = new ConcurrentQueue<H.Socket>();
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key() },
                new H.Http(), () => DateTimeOffset.UtcNow, (d, ct) => Task.Delay(d, ct));
            PlayServGameServer.Uplink.SocketFactory = () => { var s = new H.Socket(H.Ack()); _sockets.Enqueue(s); return s; };
        }
        [TearDown] public void Teardown() => PlayServGameServer.CancelForModuleShutdown();

        [UnityTest] public IEnumerator ExactFramesAreFrozenAndNotReplayed() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var payload = new Payload { Round = 7, Message = "game-owned-data" };
            var sent = PlayServGameServer.Events.PublishAsync("prj_demo:arena:one", "round_finished", payload);
            payload.Round = 99; await sent;
            var socket = _sockets.Single();
            Assert.That(socket.Sent.Last(), Is.EqualTo("{\"type\":\"publish\",\"group\":\"prj_demo:arena:one\",\"event_type\":\"round_finished\",\"data\":{\"Round\":7,\"Message\":\"game-owned-data\"},\"reliable\":false}"));
            await PlayServGameServer.Events.PublishAsync("prj_demo:arena:one", "round_finished", payload, true);
            Assert.That(socket.Sent.Last(), Does.Contain("\"reliable\":true"));
            // Round-trip the exact payload the backend places in the client event envelope.
            var frame = GameServerServiceValues.Object(socket.Sent.Last());
            var clientData = PlayServGameServerJson.Deserialize<Payload>(PlayServGameServerJson.Serialize(frame["data"]));
            Assert.That(clientData.Round, Is.EqualTo(99)); Assert.That(clientData.Message, Is.EqualTo("game-owned-data"));
            socket.Abort();
            await H.Until(() => _sockets.Count == 2 && PlayServGameServer.Uplink.State == PlayServUplinkState.Connected);
            Assert.That(_sockets.Last().Sent.Any(s => s.Contains("\"publish\"")), Is.False);
        });
        [UnityTest] public IEnumerator DisconnectedDoesNotConnectAndCallerCancellationDoesNotSend() => H.Run(async () =>
        {
            Assert.That((await H.Error(() => PlayServGameServer.Events.PublishAsync("prj_demo:one", "event", 1))).UnifiedError.SourceCode,
                Is.EqualTo("uplink_not_connected"));
            Assert.That(_sockets, Is.Empty);
            await PlayServGameServer.Uplink.ConnectAsync();
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => PlayServGameServer.Events.PublishAsync("prj_demo:one", "event", 1, ct: cancel.Token).GetAwaiter().GetResult());
            Assert.That(_sockets.Single().Sent.Count, Is.EqualTo(1));
        });
        [TestCase("arena")][TestCase("prj_:one")][TestCase("prj_demo:")][TestCase("prj_demo:one\n")]
        public void CanonicalGroupRequired(string group) =>
            Assert.Throws<ArgumentException>(() => PlayServGameServer.Events.PublishAsync(group, "event", 1).GetAwaiter().GetResult());
        [UnityTest] public IEnumerator FrameLimitCountsUtf8AndSerializerExceptionsAreSafe() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            const string group = "prj_demo:one", eventType = "event";
            var overhead = GameServerUplinkServices.Frame(new { type = "publish", group, event_type = eventType, data = "", reliable = false }).Length;
            await PlayServGameServer.Events.PublishAsync(group, eventType, new string('x', 1024 * 1024 - overhead));
            Assert.That(Encoding.UTF8.GetByteCount(_sockets.Single().Sent.Last()), Is.EqualTo(1024 * 1024));
            Assert.That((await H.Error(() => PlayServGameServer.Events.PublishAsync(group, eventType, new string('x', 1024 * 1024 - overhead + 1)))).UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            Assert.That((await H.Error(() => PlayServGameServer.Events.PublishAsync(group, eventType, new string('ї', 1024 * 1024)))).UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            var error = await H.Error(() => PlayServGameServer.Events.PublishAsync(group, eventType, new Broken()));
            Assert.That(error.ToString(), Does.Not.Contain("sk_secret"));
            Assert.That(_sockets.Single().Sent.Count, Is.EqualTo(2));
        });
        private sealed class Payload { public int Round; public string Message; }
        private sealed class Broken { public string Value => throw new Exception("sk_secret"); }
    }
}
