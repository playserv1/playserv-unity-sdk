using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Wrapper;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServGameServerLogTests
    {
        private H.Socket _socket;
        private ControlledSocket _controlled;
        private H.Http _http;
        [SetUp] public void Setup() => Configure();
        private void Configure(int count = 256, int bytes = 1024 * 1024, int timeoutMs = 3000)
        {
            PlayServGameServer.CancelForModuleShutdown(); _socket = new H.Socket(H.Ack()); _http = new H.Http();
            _controlled = new ControlledSocket(_socket);
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(),
                LogQueueCapacity = count, LogQueueMaxBytes = bytes, HttpTimeout = TimeSpan.FromMilliseconds(timeoutMs)
            }, _http, () => DateTimeOffset.UtcNow, (d, ct) => Task.Delay(d, ct));
            PlayServGameServer.Uplink.SocketFactory = () => _controlled;
        }
        [TearDown] public void Teardown() => PlayServGameServer.CancelForModuleShutdown();
        [UnityTest] public IEnumerator RedactionAndSnapshotHappenBeforeEnqueue() => H.Run(async () =>
        {
            var data = new Dictionary<string, object>
            {
                ["round"] = 1, ["Authorization"] = "opaque-secret", ["token"] = "opaque-token",
                ["nested"] = new object[] { "sk_secret", "pk_secret", "a.b.c", "rsv_secret",
                    "{\"password\":\"opaque-password\"}", "{broken sk_bad" }, ["sk_keysecret"] = 7
            };
            Assert.That(PlayServGameServer.Logs.TryWrite("Bearer sk_secret at round 1; token=opaque-token", PlayServServerLogLevel.Warning, data), Is.True);
            data["round"] = 2;
            Assert.That(_socket.Sent, Is.Empty);
            var offline = await PlayServGameServer.Logs.FlushAsync();
            Assert.That(offline.Error.SourceCode, Is.EqualTo("uplink_not_connected")); Assert.That(offline.Pending, Is.EqualTo(1));
            await PlayServGameServer.Uplink.ConnectAsync();
            var result = await PlayServGameServer.Logs.FlushAsync();
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.Sent, Is.EqualTo(1));
            var frame = _socket.Sent.Last();
            foreach (var secret in new[] { "sk_secret", "pk_secret", "a.b.c", "rsv_secret", "sk_bad", "sk_keysecret", "opaque-secret", "opaque-token", "opaque-password" })
                Assert.That(frame, Does.Not.Contain(secret));
            Assert.That(frame, Does.Contain("\"type\":\"log\"").And.Contain("\"level\":\"warning\"").And.Contain("\"round\":1").And.Contain("[REDACTED]"));
            Assert.That(PlayServGameServer.Logs.PendingBytes, Is.Zero);
        });
        [Test] public void CountByteAndFrameBoundsAndReset()
        {
            Configure(count: 1);
            Assert.That(PlayServGameServer.Logs.TryWrite("one"), Is.True);
            Assert.That(PlayServGameServer.Logs.TryWrite("two"), Is.False);
            Assert.That(PlayServGameServer.Logs.DroppedCount, Is.EqualTo(1));
            Configure(bytes: 80);
            Assert.That(PlayServGameServer.Logs.TryWrite(new string('ї', 50)), Is.False);
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero);
            Configure(bytes: 2 * 1024 * 1024);
            Assert.That(PlayServGameServer.Logs.TryWrite(new string('x', 1024 * 1024)), Is.False);
            Assert.That(PlayServGameServer.Logs.DroppedCount, Is.EqualTo(1));
            PlayServGameServer.CancelForModuleShutdown();
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero); Assert.That(PlayServGameServer.Logs.DroppedCount, Is.Zero);
        }
        [UnityTest] public IEnumerator SingleFlightKeepsNewEntriesAndCancelOnlyCancelsWaiter() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            PlayServGameServer.Logs.TryWrite("one"); _controlled.BlockLogs = true;
            using var cancel = new CancellationTokenSource();
            var first = PlayServGameServer.Logs.FlushAsync(cancel.Token);
            await H.Until(() => _controlled.LogAttempts == 1);
            var second = PlayServGameServer.Logs.FlushAsync();
            PlayServGameServer.Logs.TryWrite("two"); cancel.Cancel();
            try { await first; Assert.Fail(); } catch (OperationCanceledException) { }
            _controlled.Release.TrySetResult(true);
            var result = await second;
            Assert.That(result.Sent, Is.EqualTo(1)); Assert.That(result.Pending, Is.EqualTo(1));
            Assert.That(_controlled.LogAttempts, Is.EqualTo(1));
            Assert.That((await PlayServGameServer.Logs.FlushAsync()).Sent, Is.EqualTo(1));
            Assert.That(_controlled.LogAttempts, Is.EqualTo(2));
        });
        [UnityTest] public IEnumerator AmbiguousSendIsDiscardedAndNotRetried() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync(); _controlled.FailLogs = true;
            PlayServGameServer.Logs.TryWrite("one"); PlayServGameServer.Logs.TryWrite("two");
            var result = await PlayServGameServer.Logs.FlushAsync();
            Assert.That(result.Dropped, Is.EqualTo(1)); Assert.That(result.Pending, Is.EqualTo(1));
            Assert.That(result.Error.IsError, Is.True);
            Assert.That(result.Error.Message + result.Error.RawDetails, Does.Not.Contain("sk_failure"));
            Assert.That(_controlled.LogAttempts, Is.EqualTo(1)); Assert.That(PlayServGameServer.Logs.DroppedCount, Is.EqualTo(1));
        });
        [UnityTest] public IEnumerator BoundedTimeoutAndResetDoNotTouchNewQueue() => H.Run(async () =>
        {
            Configure(timeoutMs: 80); await PlayServGameServer.Uplink.ConnectAsync();
            _controlled.BlockLogs = true; PlayServGameServer.Logs.TryWrite("one");
            var result = await PlayServGameServer.Logs.FlushAsync();
            Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.Timeout)); Assert.That(result.Dropped, Is.EqualTo(1));
            var old = _controlled;
            Configure(); await PlayServGameServer.Uplink.ConnectAsync();
            _controlled.BlockLogs = true; PlayServGameServer.Logs.TryWrite("old");
            var pending = PlayServGameServer.Logs.FlushAsync();
            await H.Until(() => _controlled.LogAttempts == 1);
            var resetSocket = _controlled;
            PlayServGameServer.CancelForModuleShutdown(); Configure();
            PlayServGameServer.Logs.TryWrite("new"); await pending;
            old.Release.TrySetResult(true); resetSocket.Release.TrySetResult(true);
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.EqualTo(1));
            Assert.That(PlayServGameServer.Logs.DroppedCount, Is.Zero);
        });
        [UnityTest] public IEnumerator ShutdownFlushPreservesRoomResultsAndExposesFailure() => H.Run(async () =>
        {
            await H.Start("one"); PlayServGameServer.Logs.TryWrite("closing");
            var result = await PlayServGameServer.ShutdownAsync();
            Assert.That(result.LogsError.IsError, Is.False); Assert.That(result.Rooms.Count, Is.EqualTo(1));
            Assert.That(_socket.Sent.Any(s => s.Contains("\"type\":\"log\"")), Is.True);
            Configure(); await H.Start("two"); PlayServGameServer.Logs.TryWrite("closing"); _controlled.FailLogs = true;
            var failed = await PlayServGameServer.ShutdownAsync();
            Assert.That(failed.LogsError.IsError, Is.True); Assert.That(failed.IsSuccess, Is.False);
            Assert.That(failed.Rooms.Count, Is.EqualTo(1)); Assert.That(failed.Rooms[0].IsSuccess, Is.True);
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero);
        });
        [UnityTest] public IEnumerator PreCancellationAndSerializationFailuresAreSafe() => H.Run(async () =>
        {
            PlayServGameServer.Logs.TryWrite("one");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => PlayServGameServer.Logs.FlushAsync(cancel.Token));
            var e = await H.Error(() => { PlayServGameServer.Logs.TryWrite("bad", data: new Broken()); return Task.CompletedTask; });
            Assert.That(e.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Serialization));
            Assert.That(e.ToString(), Does.Not.Contain("sk_secret"));
            Assert.That(_socket.Sent, Is.Empty);
        });
        private sealed class Broken { public string Value => throw new Exception("sk_secret"); }
        private sealed class ControlledSocket : IPlayServUplinkSocket
        {
            private readonly H.Socket _inner;
            internal volatile bool BlockLogs, FailLogs;
            internal int LogAttempts;
            internal readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal ControlledSocket(H.Socket inner) { _inner = inner; }
            public Task ConnectAsync(Uri uri, string credential, CancellationToken ct) => _inner.ConnectAsync(uri, credential, ct);
            public async Task SendAsync(byte[] bytes, CancellationToken ct)
            {
                if (Encoding.UTF8.GetString(bytes).Contains("\"type\":\"log\""))
                {
                    Interlocked.Increment(ref LogAttempts);
                    if (BlockLogs) await Release.Task; // Deliberately ignores cancellation, like a stalled custom transport.
                    if (FailLogs) throw new Exception("sk_failure");
                }
                await _inner.SendAsync(bytes, ct);
            }
            public Task<string> ReceiveAsync(CancellationToken ct) => _inner.ReceiveAsync(ct);
            public void Abort() => _inner.Abort();
            public void Dispose() => _inner.Dispose();
        }
    }
}
