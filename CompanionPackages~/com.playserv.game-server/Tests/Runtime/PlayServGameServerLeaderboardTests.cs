using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Wrapper;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServGameServerLeaderboardTests
    {
        private H.Socket _socket;
        private H.Http _http;
        [SetUp] public void Setup() => Configure(TimeSpan.FromSeconds(3));
        private void Configure(TimeSpan timeout)
        {
            PlayServGameServer.CancelForModuleShutdown(); _socket = new H.Socket(H.Ack()); _http = new H.Http();
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(), HttpTimeout = timeout },
                _http, () => DateTimeOffset.UtcNow, (d, ct) => Task.Delay(d, ct));
            PlayServGameServer.Uplink.SocketFactory = () => _socket;
        }
        [TearDown] public void Teardown() => PlayServGameServer.CancelForModuleShutdown();
        private string[] QueryIds() => _socket.Sent.Where(s => s.Contains("\"leaderboard_query\""))
            .Select(s => (string)GameServerServiceValues.Object(s)["request_id"]).ToArray();
        private void Reply(string id, string entries = "[]") => _socket.Push(
            "{\"type\":\"leaderboard_result\",\"request_id\":\"" + id + "\",\"entries\":" + entries + "}");
        private const string Entry = "{\"rank\":101,\"player_id\":\"plr_viewer\",\"display_name\":null,\"score\":9223372036854775807,\"recorded_at\":\"2026-09-12T00:00:00Z\"}";

        [UnityTest] public IEnumerator ScoreWireSnapshotAndOptionalKey() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var metadata = new Dictionary<string, object> { ["round"] = 3 };
            var sent = PlayServGameServer.Leaderboards.SubmitScoreAsync("plr_one", long.MinValue, metadata, "caller-key");
            metadata["round"] = 4; await sent;
            Assert.That(_socket.Sent.Last(), Is.EqualTo("{\"type\":\"score_submit\",\"player_id\":\"plr_one\",\"score\":-9223372036854775808,\"metadata\":{\"round\":3},\"idempotency_key\":\"caller-key\"}"));
            await PlayServGameServer.Leaderboards.SubmitScoreAsync("plr_one", 42);
            Assert.That(_socket.Sent.Last(), Does.Contain("\"metadata\":null").And.Contain("\"idempotency_key\":null"));
            Assert.That(_http.Requests, Is.Empty);
        });
        [UnityTest] public IEnumerator ConcurrentOutOfOrderTopAndViewerAreImmutable() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var first = PlayServGameServer.Leaderboards.GetTopAsync();
            var second = PlayServGameServer.Leaderboards.GetTopAsync(100, "plr_viewer");
            var ids = QueryIds(); Assert.That(ids.Distinct().Count(), Is.EqualTo(2));
            Assert.That(_socket.Sent.Last(), Does.Contain("\"top\":100").And.Contain("\"viewer_player_id\":\"plr_viewer\""));
            Reply(ids[1], "[" + Entry + "]"); var page = await second;
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(page.Entries.Single().Score, Is.EqualTo(long.MaxValue));
            Assert.That(page.Entries.Single().Rank, Is.EqualTo(101));
            Assert.That(page.Entries.Single().RecordedAt, Is.EqualTo(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)));
            Assert.Throws<NotSupportedException>(() => ((IList<PlayServServerLeaderboardEntry>)page.Entries).Clear());
            Reply(ids[0]); Assert.That((await first).Entries, Is.Empty);
            Assert.That(PlayServGameServer.Uplink.PendingServiceQueryCount, Is.Zero);
            Assert.That(_http.Requests, Is.Empty);
        });
        [UnityTest] public IEnumerator InvalidResponseAffectsOnlyItsRequestAndNeverLeaksPayload() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            foreach (var entries in new[] { "null", "{}", "[{}]", "[" + Entry.Replace("101", "1.5") + "]",
                "[" + Entry.Replace("9223372036854775807", "9223372036854775808") + "]",
                "[" + Entry.Replace("null", "123") + "]", "[" + Entry.Replace("2026-09-12T00:00:00Z", "sk_secret") + "]" })
            {
                var request = PlayServGameServer.Leaderboards.GetTopAsync(); Reply(QueryIds().Last(), entries);
                var e = await H.Error(async () => await request);
                Assert.That(e.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
                Assert.That(e.ToString(), Does.Not.Contain("sk_secret"));
            }
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Connected));
        });
        [UnityTest] public IEnumerator CancellationTimeoutAndLateRepliesCleanUp() => H.Run(async () =>
        {
            Configure(TimeSpan.FromMilliseconds(80)); await PlayServGameServer.Uplink.ConnectAsync();
            using var cancel = new CancellationTokenSource();
            var canceled = PlayServGameServer.Leaderboards.GetTopAsync(ct: cancel.Token);
            var oldId = QueryIds().Last(); cancel.Cancel();
            try { await canceled; Assert.Fail(); } catch (OperationCanceledException) { }
            Assert.That(PlayServGameServer.Uplink.PendingServiceQueryCount, Is.Zero);
            Reply(oldId);
            var timed = await H.Error(() => PlayServGameServer.Leaderboards.GetTopAsync());
            Assert.That(timed.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(timed.UnifiedError.Retryable, Is.True);
            Assert.That(PlayServGameServer.Uplink.PendingServiceQueryCount, Is.Zero);
            Assert.That(QueryIds().Length, Is.EqualTo(2));
        });
        [UnityTest] public IEnumerator DisconnectShutdownAndBoundedPending() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var pending = new List<Task<PlayServServerLeaderboardResult>>();
            for (var i = 0; i < 128; i++) pending.Add(PlayServGameServer.Leaderboards.GetTopAsync());
            Assert.That((await H.Error(() => PlayServGameServer.Leaderboards.GetTopAsync())).UnifiedError.SourceCode, Is.EqualTo("uplink_pending_limit"));
            await PlayServGameServer.Uplink.DisconnectAsync();
            foreach (var p in pending) await H.Error(async () => await p);
            Assert.That(PlayServGameServer.Uplink.PendingServiceQueryCount, Is.Zero);
            Configure(TimeSpan.FromSeconds(3)); await PlayServGameServer.Uplink.ConnectAsync();
            var last = PlayServGameServer.Leaderboards.GetTopAsync();
            await PlayServGameServer.ShutdownAsync(); await H.Error(async () => await last);
            Assert.That(PlayServGameServer.Uplink.PendingServiceQueryCount, Is.Zero);
        });
        [UnityTest] public IEnumerator OversizeRefusalIsCorrelated() => H.Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var first = PlayServGameServer.Leaderboards.GetTopAsync();
            var second = PlayServGameServer.Leaderboards.GetTopAsync();
            var ids = QueryIds();
            _socket.Push("{\"type\":\"frame_too_large\",\"request_id\":\"" + ids[0] + "\"}");
            Assert.That((await H.Error(async () => await first)).UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            Assert.That(second.IsCompleted, Is.False); Reply(ids[1]); await second;
            var oversized = PlayServGameServer.Leaderboards.GetTopAsync();
            _socket.Push(new string('x', 1024 * 1024 + 1));
            Assert.That((await H.Error(async () => await oversized)).UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            Assert.That(PlayServGameServer.Uplink.PendingServiceQueryCount, Is.Zero);
        });
        [Test] public void ValidationDoesNotConnect()
        {
            foreach (var top in new[] { 0, 101 })
                Assert.Throws<ArgumentOutOfRangeException>(() => PlayServGameServer.Leaderboards.GetTopAsync(top));
            Assert.Throws<ArgumentException>(() => PlayServGameServer.Leaderboards.SubmitScoreAsync("", 1));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => PlayServGameServer.Leaderboards.GetTopAsync(ct: cancel.Token));
            Assert.That(_socket.Sent, Is.Empty); Assert.That(_http.Requests, Is.Empty);
        }
    }
}
