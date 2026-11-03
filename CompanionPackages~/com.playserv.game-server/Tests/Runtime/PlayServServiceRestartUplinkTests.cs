using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;
using static Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServServiceRestartUplinkTests
    {
        private readonly ConcurrentQueue<Wire> _wires = new ConcurrentQueue<Wire>();
        private readonly List<PlayServGameRoomHandle> _rooms = new List<PlayServGameRoomHandle>();
        private readonly ConcurrentQueue<PlayServUplinkState> _states = new ConcurrentQueue<PlayServUplinkState>();
        private PlayServGameServerUplinkTests.Http _http;
        private DelayGate _delays;
        private PlayServGameServerUplink _uplink;
        private int _failedDials;

        [SetUp]
        public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown();
            while (_wires.TryDequeue(out _)) { }
            while (_states.TryDequeue(out _)) { }
            _rooms.Clear(); _failedDials = 0;
            _http = new PlayServGameServerUplinkTests.Http(); _delays = new DelayGate();
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena",
                InstanceId = "restart-instance", ServerKeyProvider = new Key()
            }, _http, () => DateTimeOffset.FromUnixTimeMilliseconds(1000), (_, ct) => Task.Delay(Timeout.Infinite, ct));
            var context = PlayServGameServer.GetContextForServices();
            var delivery = SynchronizationContext.Current;
            Assert.That(delivery, Is.Not.Null);
            context.Dispatch = action => delivery.Post(_ => action(), null);
            context.MonotonicSeconds = () => 100;
            _uplink = PlayServGameServer.Uplink;
            _uplink.Seconds = () => 100;
            _uplink.Delay = _delays.Wait;
            _uplink.StateChanged += _states.Enqueue;
            _uplink.SocketFactory = () =>
            {
                var wire = new Wire();
                wire.Push(Ack());
                _wires.Enqueue(wire);
                return new DialAdapter(wire, _wires.Count <= _failedDials);
            };
        }

        [UnityTearDown]
        public IEnumerator Cleanup() => Run(async () =>
        {
            try
            {
                await _uplink.DisconnectAsync();
                await Task.WhenAll(_rooms.Select(room => room.DrainPresenceForTesting()));
            }
            finally { PlayServGameServer.CancelForModuleShutdown(); }
        });

        [UnityTest]
        public IEnumerator ServiceRestart1012_rehellos_and_preserves_all_rooms_and_rosters() => Run(async () =>
        {
            var one = await Start("one"); _rooms.Add(one);
            var two = await Start("two"); _rooms.Add(two);
            var connected = _wires.Single();
            await AdmitAndAcknowledge(connected.Push, connected.Sent, one, "plr_one");
            await AdmitAndAcknowledge(connected.Push, connected.Sent, two, "plr_two");
            var terminations = 0; var closures = 0;
            foreach (var room in _rooms)
            {
                room.Terminated += (_, __) => terminations++;
                room.LifetimeClosed += (_, __) => closures++;
            }

            for (var restart = 1; restart <= 2; restart++)
            {
                var retry = await CloseAndAwaitRetry(_wires.Last());
                Assert.That(retry.Milliseconds, Is.InRange(125, 250));
                Assert.That(_rooms.All(room => room.State == PlayServGameRoomState.Active), Is.True);
                retry.Release();
                var expected = restart + 1;
                await Until(() => _wires.Count == expected && _uplink.State == PlayServUplinkState.Connected);
                var recovered = _wires.Last();
                await Until(() => recovered.Sent.Count(frame => frame.Contains("\"event\":\"roster\"")) == 2);
                var hello = recovered.Sent.First();
                Assert.That(hello, Does.Contain("\"type\":\"uplink_hello\""));
                Assert.That(hello, Does.Contain("\"instance_id\":\"restart-instance\""));
                Assert.That(hello, Does.Contain("\"executor_slug\":\"arena\""));
                Assert.That(hello, Does.Contain("\"resume\":true"));
                Assert.That(recovered.Sent.Any(frame => frame.Contains("\"room_name\":\"one\"") && frame.Contains("plr_one")), Is.True);
                Assert.That(recovered.Sent.Any(frame => frame.Contains("\"room_name\":\"two\"") && frame.Contains("plr_two")), Is.True);
                Assert.That(recovered.Sent.Any(frame => frame.Contains("\"event\":\"join\"") || frame.Contains("rsv_")), Is.False);
                Assert.That(_rooms.All(room => room.State == PlayServGameRoomState.Active), Is.True);
            }
            await Until(() => _states.Count(state => state == PlayServUplinkState.Connected) == 3);
            Assert.That(_states.Count(state => state == PlayServUplinkState.Reconnecting), Is.EqualTo(2));
            Assert.That(_states, Has.No.Member(PlayServUplinkState.Terminated));
            Assert.That(terminations, Is.Zero);
            Assert.That(closures, Is.Zero);
            Assert.That(_http.Requests.Any(request => request.RelativePath.Contains(":close")), Is.False);
            one.ReportLeave("plr_one");
            await one.DrainPresenceForTesting();
            Assert.That(_wires.Last().Sent.Last(), Does.Contain("\"event\":\"leave\""));
        });

        [UnityTest]
        public IEnumerator ServiceRestart1012_resets_backoff_after_success_even_after_failed_dials() => Run(async () =>
        {
            _failedDials = 3;
            var connect = _uplink.ConnectAsync();
            for (var failure = 0; failure < _failedDials; failure++)
            {
                var expected = failure + 1;
                await Until(() => _delays.Calls.Count == expected);
                var retry = _delays.Calls.Last();
                var cap = 250 << failure;
                Assert.That(retry.Milliseconds, Is.InRange(cap / 2, cap));
                retry.Release();
            }
            var error = await Error(() => connect);
            Assert.That(error.UnifiedError.Retryable, Is.True);
            await Until(() => _uplink.State == PlayServUplinkState.Connected && _delays.Calls.Count == 4);
            for (var restart = 0; restart < 3; restart++)
            {
                var previousSockets = _wires.Count;
                var retry = await CloseAndAwaitRetry(_wires.Last());
                Assert.That(retry.Milliseconds, Is.InRange(125, 250), "Established connection must reset the failed-dial backoff.");
                var previousDelays = _delays.Calls.Count;
                retry.Release();
                await Until(() => _wires.Count == previousSockets + 1 && _uplink.State == PlayServUplinkState.Connected && _delays.Calls.Count == previousDelays + 1);
            }
        });

        [UnityTest]
        public IEnumerator PolicyViolation1008_remains_terminal_and_does_not_redial() => Run(async () =>
        {
            var room = await Start("one"); _rooms.Add(room);
            var terminations = 0;
            room.Terminated += (_, __) => terminations++;
            _wires.Single().CloseFromPlatform(1008);
            await Until(() => room.State == PlayServGameRoomState.Terminated && terminations == 1 && _delays.Calls.All(call => call.Completion.IsCompleted));
            Assert.That(_uplink.State, Is.EqualTo(PlayServUplinkState.Terminated));
            Assert.That(_wires.Count, Is.EqualTo(1));
            Assert.That(_delays.Calls.Count, Is.EqualTo(1), "Only the canceled connection monitor may have waited.");
            Assert.That(_states, Has.No.Member(PlayServUplinkState.Reconnecting));
        });

        private async Task<DelayGate.Pending> CloseAndAwaitRetry(Wire wire)
        {
            await Until(() => _uplink.State == PlayServUplinkState.Connected &&
                _delays.Calls.Any(call => !call.Completion.IsCompleted));
            var before = _delays.Calls.ToArray();
            Assert.That(before.Last().Completion.IsCompleted, Is.False, "Connected monitor must be waiting.");
            wire.CloseFromPlatform(1012);
            await Until(() => _delays.Calls.Count > before.Length);
            Assert.That(_uplink.State, Is.EqualTo(PlayServUplinkState.Reconnecting));
            Assert.That(before.Last().Completion.IsCanceled, Is.True, "Disconnect must cancel the old monitor before the retry delay.");
            Assert.That(_delays.Calls.Count, Is.EqualTo(before.Length + 1));
            return _delays.Calls.Last();
        }

        private sealed class DelayGate
        {
            internal readonly ConcurrentQueue<Pending> Calls = new ConcurrentQueue<Pending>();
            internal async Task Wait(int milliseconds, CancellationToken ct)
            {
                var pending = new Pending(milliseconds);
                using (ct.Register(() => pending.Cancel()))
                {
                    Calls.Enqueue(pending);
                    await pending.Completion.ConfigureAwait(false);
                }
            }
            internal sealed class Pending
            {
                private readonly TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                internal readonly int Milliseconds;
                internal Pending(int milliseconds) { Milliseconds = milliseconds; }
                internal Task Completion => _done.Task;
                internal void Release() => _done.TrySetResult(true);
                internal void Cancel() => _done.TrySetCanceled();
            }
        }

        // Only dialing is simulated: real framing and close classification stay in PlayServUplinkSocket.
        private sealed class DialAdapter : IPlayServUplinkSocket
        {
            private readonly PlayServUplinkSocket _socket;
            private readonly bool _failDial;
            internal DialAdapter(Wire wire, bool failDial) { _socket = new PlayServUplinkSocket(wire); _failDial = failDial; }
            public Task ConnectAsync(Uri endpoint, string credential, CancellationToken ct) =>
                _failDial ? Task.FromException(new IOException("Fixture dial failed.")) : Task.CompletedTask;
            public Task SendAsync(byte[] bytes, CancellationToken ct) => _socket.SendAsync(bytes, ct);
            public Task<string> ReceiveAsync(CancellationToken ct) => _socket.ReceiveAsync(ct);
            public void Abort() => _socket.Abort();
            public void Dispose() => _socket.Dispose();
        }

        private sealed class Wire : WebSocket
        {
            private readonly ConcurrentQueue<object> _received = new ConcurrentQueue<object>();
            private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
            private readonly CancellationTokenSource _aborted = new CancellationTokenSource();
            internal readonly ConcurrentQueue<string> Sent = new ConcurrentQueue<string>();
            private WebSocketCloseStatus? _close;
            public override WebSocketCloseStatus? CloseStatus => _close;
            public override string CloseStatusDescription => "service_restart";
            public override string SubProtocol => null;
            public override WebSocketState State => _aborted.IsCancellationRequested ? WebSocketState.Aborted : WebSocketState.Open;
            internal void Push(string text) { _received.Enqueue(text); _available.Release(); }
            internal void CloseFromPlatform(int code) { _received.Enqueue(code); _available.Release(); }
            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _aborted.Token);
                await _available.WaitAsync(linked.Token).ConfigureAwait(false);
                _received.TryDequeue(out var frame);
                if (frame is int code)
                {
                    _close = (WebSocketCloseStatus)code;
                    return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, _close, CloseStatusDescription);
                }
                var bytes = Encoding.UTF8.GetBytes((string)frame);
                Assert.That(bytes.Length, Is.LessThanOrEqualTo(buffer.Count));
                Array.Copy(bytes, 0, buffer.Array, buffer.Offset, bytes.Length);
                return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
            }
            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Sent.Enqueue(Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count));
                return Task.CompletedTask;
            }
            public override void Abort() => _aborted.Cancel();
            public override void Dispose() => Abort();
            public override Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken ct) => Task.CompletedTask;
            public override Task CloseOutputAsync(WebSocketCloseStatus status, string reason, CancellationToken ct) => Task.CompletedTask;
        }
    }
}
