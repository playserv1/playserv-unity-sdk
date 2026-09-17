using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Data;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServGameServerUplinkDataTests
    {
        private H.Socket _socket;
        private H.Http _http;
        private PlayServGameServerUplink _uplink;
        private ControlledSocket _controlled;

        [SetUp] public void Setup() => Configure(TimeSpan.FromSeconds(3));

        private void Configure(TimeSpan timeout)
        {
            PlayServGameServer.CancelForModuleShutdown();
            _socket = new H.Socket(H.Ack());
            _http = new H.Http { Handler = (_, __) => Task.FromResult(Catalogue()) };
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena",
                ServerKeyProvider = new H.Key(), HttpTimeout = timeout
            }, _http, () => DateTimeOffset.UtcNow, (delay, ct) => Task.Delay(delay, ct));
            _uplink = PlayServGameServer.Uplink;
            _controlled = new ControlledSocket(_socket);
            _uplink.SocketFactory = () => _controlled;
        }

        [UnityTearDown] public IEnumerator Cleanup() => H.Run(async () =>
        {
            await _uplink.DisconnectAsync();
            PlayServGameServer.CancelForModuleShutdown();
        });

        [UnityTest] public IEnumerator QuerySendsScopedFrameAndDeserializesDataResult() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var query = _uplink.Data.QueryAsync<Profile>("Profiles", " player-key ");
            await H.Until(() => Frames("data_query").Length == 1);
            var frame = GameServerServiceValues.Object(Frames("data_query").Single());
            Assert.That(frame.Keys, Is.EquivalentTo(new[] { "type", "request_id", "entity", "id" }));
            Assert.That(frame["entity"], Is.EqualTo("Profiles"));
            Assert.That(frame["id"], Is.EqualTo(" player-key "));
            _socket.Push("{\"type\":\"data_result\",\"request_id\":\"" + frame["request_id"] +
                "\",\"entity\":\"Profiles\",\"id\":\" player-key \",\"found\":true,\"data\":{\"display_name\":\"Ada\"}}");
            var result = await query;
            Assert.That(result.Entity, Is.EqualTo("Profiles"));
            Assert.That(result.Key, Is.EqualTo(" player-key "));
            Assert.That(result.Found, Is.True);
            Assert.That(result.Value.Name, Is.EqualTo("Ada"));
            AssertCatalogueOnly();
        });

        [UnityTest] public IEnumerator MutationFramesSnapshotBeforeCatalogueAndCompleteOnSend() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var catalogue = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, __) => catalogue.Task;
            var value = new Profile { Name = "before" };
            var upsert = _uplink.Data.SendUpsertAsync("Profiles", "key", value);
            value.Name = "after";
            catalogue.SetResult(Catalogue());
            await upsert;
            await _uplink.Data.SendIncrementAsync("Profiles", "key", new Dictionary<string, long> { ["Score"] = long.MinValue });
            await _uplink.Data.SendDeleteAsync("Profiles", "key");
            var frames = Frames("data_write");
            Assert.That(frames.Length, Is.EqualTo(3));
            Assert.That(frames[0], Is.EqualTo("{\"type\":\"data_write\",\"entity\":\"Profiles\",\"op\":\"upsert\",\"id\":\"key\",\"data\":{\"display_name\":\"before\"}}"));
            Assert.That(frames[1], Is.EqualTo("{\"type\":\"data_write\",\"entity\":\"Profiles\",\"op\":\"increment\",\"id\":\"key\",\"data\":{\"Score\":-9223372036854775808}}"));
            Assert.That(frames[2], Is.EqualTo("{\"type\":\"data_write\",\"entity\":\"Profiles\",\"op\":\"delete\",\"id\":\"key\",\"data\":{}}"));
            AssertCatalogueOnly();
        });

        [UnityTest] public IEnumerator ConcurrentQueriesCorrelateOutOfOrderAndIgnoreUnrelatedReplies() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var first = Query("first");
            var second = Query("second");
            await H.Until(() => Frames("data_query").Length == 2);
            var ids = QueryIds();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(2));
            Reply("unknown", "first");
            Reply(ids[1], "second");
            await second;
            Assert.That(first.IsCompleted, Is.False);
            Reply(ids[0], "first", found: false);
            var missing = await first;
            Assert.That(missing.Found, Is.False);
            Assert.That(missing.Value, Is.Null);
        });

        [UnityTest] public IEnumerator InvalidReplyOnlyFailsItsQueryAndDoesNotExposePayload() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            foreach (var invalid in new[]
            {
                "\"entity\":\"Other\",\"id\":\"key\",\"found\":true,\"data\":{}",
                "\"entity\":\"Profiles\",\"id\":\"other\",\"found\":true,\"data\":{}",
                "\"entity\":\"Profiles\",\"id\":\"key\",\"found\":\"true\",\"data\":{}",
                "\"entity\":\"Profiles\",\"id\":\"key\",\"data\":{}",
                "\"entity\":\"Profiles\",\"id\":\"key\",\"found\":true,\"data\":null",
                "\"entity\":\"Profiles\",\"id\":\"key\",\"found\":false,\"data\":[]",
                "\"entity\":\"Profiles\",\"id\":\"key\",\"found\":true,\"data\":{\"display_name\":{\"secret\":\"sk_secret\"}}"
            })
            {
                var request = Query();
                var id = QueryIds().Last();
                _socket.Push("{\"type\":\"data_result\",\"request_id\":\"" + id + "\"," + invalid + "}");
                var error = await H.Error(() => request);
                Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
                Assert.That(error.ToString(), Does.Not.Contain("sk_secret"));
            }
            Assert.That(_uplink.State, Is.EqualTo(PlayServUplinkState.Connected));
        });

        [UnityTest] public IEnumerator ServerAclDenialRefreshesAndRejectsBeforeAnyFrame() => H.Run(async () =>
        {
            _http.Handler = (_, __) => Task.FromResult(Catalogue("false", "false"));
            await _uplink.ConnectAsync();
            foreach (var operation in new[] { PlayServDataAccessOperation.Read, PlayServDataAccessOperation.Write })
            {
                try
                {
                    await (operation == PlayServDataAccessOperation.Read ? Query() :
                        _uplink.Data.SendDeleteAsync("Profiles", "key"));
                    Assert.Fail("A known server ACL denial must be enforced locally.");
                }
                catch (PlayServGameServerException error)
                {
                    Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Forbidden));
                    Assert.That(error.UnifiedError.HttpStatus, Is.EqualTo(403));
                    Assert.That(error.UnifiedError.SourceCode, Is.EqualTo(operation == PlayServDataAccessOperation.Read
                        ? "table_read_forbidden" : "table_write_forbidden"));
                }
            }
            Assert.That(_http.Requests.Count, Is.EqualTo(3));
            Assert.That(Frames("data_query"), Is.Empty);
            Assert.That(Frames("data_write"), Is.Empty);
        });

        [UnityTest] public IEnumerator StaleAclDenialCanRefreshToAllowedAndUsesCurrentSession() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            _socket.Push("{\"type\":\"session_token\",\"session_token\":\"session-two\",\"expires_in\":3600}");
            await H.Until(async () => await _uplink.GetRestCredentialAsync(PlayServGameServer.GetContextForServices(), default) == "session-two");
            _http.Handler = (_, __) => Task.FromResult(_http.Requests.Count == 1 ? Catalogue("true", "false") : Catalogue());
            await _uplink.Data.SendDeleteAsync("Profiles", "key");
            Assert.That(_http.Requests.Count, Is.EqualTo(2));
            Assert.That(_http.Requests.All(request => request.ServerKey == "session-two"), Is.True);
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator SingletonRejectsIncrementAndDeleteButAllowsUpsertAndQuery() => H.Run(async () =>
        {
            _http.Handler = (_, __) => Task.FromResult(Catalogue(singleton: true));
            await _uplink.ConnectAsync();
            foreach (var operation in new[] { "SendIncrementAsync", "SendDeleteAsync" })
            {
                try
                {
                    await (operation == "SendDeleteAsync" ? _uplink.Data.SendDeleteAsync("Profiles", "key") :
                        _uplink.Data.SendIncrementAsync("Profiles", "key", new Dictionary<string, long> { ["Score"] = 1 }));
                    Assert.Fail("This mutation has no singleton semantics.");
                }
                catch (InvalidOperationException) { }
            }
            await _uplink.Data.SendUpsertAsync("Profiles", "self", new Profile());
            var query = Query("self");
            Reply(QueryIds().Single(), "self");
            await query;
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator ClosedAndPreCanceledOperationsDoNotPerformIo() => H.Run(async () =>
        {
            Assert.That((await H.Error(() => Query())).UnifiedError.SourceCode, Is.EqualTo("uplink_not_connected"));
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            try { await Query(ct: cancel.Token); Assert.Fail(); } catch (OperationCanceledException) { }
            Assert.That(_socket.Sent, Is.Empty);
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator ExpiredSessionDoesNotStartCatalogueOrConnect() => H.Run(async () =>
        {
            var seconds = 100d;
            _uplink.Seconds = () => seconds;
            _uplink.Delay = (_, ct) => Task.Delay(Timeout.Infinite, ct);
            await _uplink.ConnectAsync();
            seconds += 4000;
            Assert.That((await H.Error(() => Query())).UnifiedError.SourceCode, Is.EqualTo("uplink_session_expired"));
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(_socket.Sent.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator SocketExceptionsNeverExposePayloadOrCredentials() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            _controlled.SendError = new InvalidOperationException("sk_secret submitted payload");
            var error = await H.Error(() => _uplink.Data.SendDeleteAsync("Profiles", "key"));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("uplink_send_failed"));
            Assert.That(error.ToString(), Does.Not.Contain("sk_secret").And.Not.Contain("submitted payload"));
        });

        [UnityTest] public IEnumerator QueryCancellationRemovesPendingAndIgnoresLateResponse() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var cancel = new CancellationTokenSource();
            var pending = Query(ct: cancel.Token);
            var oldId = QueryIds().Single();
            cancel.Cancel();
            try { await pending; Assert.Fail(); } catch (OperationCanceledException) { }
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            Reply(oldId, "key");
            var next = Query("next");
            Assert.That(next.IsCompleted, Is.False);
            Reply(QueryIds().Last(), "next");
            await next;
            Assert.That(QueryIds().Length, Is.EqualTo(2));
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
        });

        [UnityTest] public IEnumerator QueryTimeoutCoversUncooperativeSendAndCleansPending() => H.Run(async () =>
        {
            Configure(TimeSpan.FromMilliseconds(80));
            await _uplink.ConnectAsync();
            var stalled = new TaskCompletionSource<bool>();
            _controlled.SendGate = stalled.Task;
            var error = await H.Error(() => Query());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(error.UnifiedError.Retryable, Is.True);
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            stalled.SetException(new InvalidOperationException("sk_secret late send failure"));
            Assert.That(QueryIds().Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator QueryTimeoutCoversMissingReplyAndUncooperativeCatalogue() => H.Run(async () =>
        {
            Configure(TimeSpan.FromMilliseconds(80));
            await _uplink.ConnectAsync();
            Assert.That((await H.Error(() => Query())).UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            await _uplink.DisconnectAsync();
            Configure(TimeSpan.FromMilliseconds(80));
            await _uplink.ConnectAsync();
            var catalogue = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, __) => catalogue.Task;
            Assert.That((await H.Error(() => Query())).UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            Assert.That(Frames("data_query"), Is.Empty);
            catalogue.SetResult(Catalogue());
        });

        [UnityTest] public IEnumerator QueryLimitIsIndependentFromLeaderboardsAndReleasedOnDisconnect() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var pending = new List<Task<PlayServUplinkDataResult<Profile>>>();
            for (var i = 0; i < 128; i++) pending.Add(Query(i.ToString()));
            Assert.That((await H.Error(() => Query())).UnifiedError.SourceCode, Is.EqualTo("uplink_pending_limit"));
            Assert.That(_uplink.PendingDataQueryCount, Is.EqualTo(128));
            var leaderboard = PlayServGameServer.Leaderboards.GetTopAsync();
            var id = (string)GameServerServiceValues.Object(Frames("leaderboard_query").Single())["request_id"];
            _socket.Push("{\"type\":\"leaderboard_result\",\"request_id\":\"" + id + "\",\"entries\":[]}");
            await leaderboard;
            await _uplink.DisconnectAsync();
            foreach (var query in pending) await H.Error(() => query);
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
        });

        [UnityTest] public IEnumerator DisconnectCancelsCatalogueWaitAndOldFacadeCannotSendAfterReconnect() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var old = _uplink.Data;
            var catalogue = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, __) => catalogue.Task;
            var query = old.QueryAsync<Profile>("Profiles", "key");
            var mutation = old.SendDeleteAsync("Profiles", "key");
            await H.Until(() => _http.Requests.Count == 1);
            await _uplink.DisconnectAsync();
            await H.Error(() => query);
            await H.Error(() => mutation);
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            catalogue.SetResult(Catalogue());
            _socket = new H.Socket(H.Ack());
            _controlled = new ControlledSocket(_socket);
            await _uplink.ConnectAsync();
            Assert.That((await H.Error(() => old.SendDeleteAsync("Profiles", "key"))).UnifiedError.SourceCode,
                Is.EqualTo("uplink_not_connected"));
            Assert.That(Frames("data_query"), Is.Empty);
            Assert.That(Frames("data_write"), Is.Empty);
            _http.Handler = (_, __) => Task.FromResult(Catalogue());
            await _uplink.Data.SendDeleteAsync("Profiles", "key");
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator TerminationShutdownAndReconfigurationClearQueriesAndFacades() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var terminated = Query();
            _socket.Fail(new PlayServUplinkFailure("uplink_unauthorized", true));
            Assert.That((await H.Error(() => terminated)).UnifiedError.SourceCode, Is.EqualTo("uplink_unauthorized"));
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            await _uplink.DisconnectAsync();
            Configure(TimeSpan.FromSeconds(3));
            await _uplink.ConnectAsync();
            var shutdown = Query();
            await PlayServGameServer.ShutdownAsync();
            await H.Error(() => shutdown);
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            Configure(TimeSpan.FromSeconds(3));
            await _uplink.ConnectAsync();
            var previous = _uplink;
            var facade = previous.Data;
            var replaced = Query();
            PlayServGameServer.CancelForModuleShutdown();
            await H.Error(() => replaced);
            await previous.DisconnectAsync();
            Configure(TimeSpan.FromSeconds(3));
            await _uplink.ConnectAsync();
            await H.Error(() => facade.SendDeleteAsync("Profiles", "key"));
            Assert.That(Frames("data_write"), Is.Empty);
        });

        [UnityTest] public IEnumerator OversizeReplyIsCorrelatedIndependentlyFromLeaderboards() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var data = Query();
            var leaderboard = PlayServGameServer.Leaderboards.GetTopAsync();
            _socket.Push("{\"type\":\"frame_too_large\",\"request_id\":\"" + QueryIds().Single() + "\"}");
            Assert.That((await H.Error(() => data)).UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            Assert.That(leaderboard.IsCompleted, Is.False);
            var next = Query();
            var leaderboardId = (string)GameServerServiceValues.Object(Frames("leaderboard_query").Single())["request_id"];
            _socket.Push("{\"type\":\"frame_too_large\",\"request_id\":\"" + leaderboardId + "\"}");
            Assert.That((await H.Error(() => leaderboard)).UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            Assert.That(next.IsCompleted, Is.False);
            Reply(QueryIds().Last(), "key");
            await next;
            var oversized = Query();
            _socket.Push(new string('x', 1024 * 1024 + 1));
            Assert.That((await H.Error(() => oversized)).UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
        });

        [UnityTest] public IEnumerator FrameCapCountsUtf8BytesAndAllowsExactLimit() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            const string empty = "{\"type\":\"data_write\",\"entity\":\"Profiles\",\"op\":\"upsert\",\"id\":\"key\",\"data\":{\"display_name\":\"\"}}";
            var available = 1024 * 1024 - Encoding.UTF8.GetByteCount(empty);
            await _uplink.Data.SendUpsertAsync("Profiles", "key", new Profile { Name = new string('x', available) });
            Assert.That(Encoding.UTF8.GetByteCount(Frames("data_write").Single()), Is.EqualTo(1024 * 1024));
            var error = await H.Error(() => _uplink.Data.SendUpsertAsync("Profiles", "key", new Profile { Name = new string('é', available / 2 + 1) }));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator SnapshotRetainsIsoStringsNestedValuesAndExtremeIntegers() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var catalogue = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, __) => catalogue.Task;
            var nested = new Dictionary<string, object> { ["when"] = "2026-09-17T12:00:00+03:00", ["max"] = long.MaxValue };
            var values = new Dictionary<string, object> { ["nested"] = nested };
            var upsert = _uplink.Data.SendUpsertAsync("Profiles", "key", values);
            nested["when"] = "changed";
            catalogue.SetResult(Catalogue());
            await upsert;
            Assert.That(Frames("data_write").Single(), Does.Contain("2026-09-17T12:00:00+03:00").And.Contain("9223372036854775807").And.Not.Contain("changed"));
            var query = Query("2026-09-17T12:00:00+03:00");
            var id = QueryIds().Last();
            _socket.Push("{\"type\":\"data_result\",\"request_id\":\"" + id + "\",\"entity\":\"Profiles\",\"id\":\"2026-09-17T12:00:00+03:00\",\"found\":true,\"data\":{\"display_name\":\"2026-09-17T12:00:00+03:00\"}}");
            Assert.That((await query).Value.Name, Is.EqualTo("2026-09-17T12:00:00+03:00"));
        });

        [UnityTest] public IEnumerator ValidationAndSerializationRefuseBeforeCatalogueIo() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            foreach (var invalid in new[] { "", " ", "bad\nvalue" })
            {
                Assert.Throws<ArgumentException>(() => _uplink.Data.QueryAsync<Profile>(invalid, "key"));
                Assert.Throws<ArgumentException>(() => _uplink.Data.SendDeleteAsync("Profiles", invalid));
            }
            Assert.Throws<ArgumentNullException>(() => _uplink.Data.SendUpsertAsync<object>("Profiles", "key", null));
            foreach (var value in new object[] { "{}", 4, new[] { 1, 2 } })
                Assert.Throws<ArgumentException>(() => _uplink.Data.SendUpsertAsync("Profiles", "key", value));
            Assert.Throws<ArgumentNullException>(() => _uplink.Data.SendIncrementAsync("Profiles", "key", null));
            Assert.Throws<ArgumentException>(() => _uplink.Data.SendIncrementAsync("Profiles", "key", new Dictionary<string, long>()));
            Assert.Throws<ArgumentException>(() => _uplink.Data.SendIncrementAsync("Profiles", "key", new Dictionary<string, long> { [" "] = 1 }));
            var cycle = new Dictionary<string, object>(); cycle["cycle"] = cycle;
            var error = await H.Error(() => _uplink.Data.SendUpsertAsync("Profiles", "key", cycle));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Serialization));
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(_socket.Sent.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator UnknownAclAllowsSendAndHttpFailureIsSafeAndRetryable() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            _http.Handler = (_, __) => Task.FromResult(Catalogue("null", "null"));
            await _uplink.Data.SendDeleteAsync("Profiles", "key");
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
            await _uplink.DisconnectAsync();
            Configure(TimeSpan.FromSeconds(3));
            await _uplink.ConnectAsync();
            _http.Handler = (_, __) => Task.FromResult(new PlayServGameServerHttpResponse
            { StatusCode = 503, Body = "sk_secret body" });
            var error = await H.Error(() => Query());
            Assert.That(error.UnifiedError.HttpStatus, Is.EqualTo(503));
            Assert.That(error.UnifiedError.Retryable, Is.True);
            Assert.That(error.ToString(), Does.Not.Contain("sk_secret"));
            Assert.That(Frames("data_query"), Is.Empty);
        });

        [UnityTest] public IEnumerator MissingSchemaUsesCompanionNotFoundError() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var error = await H.Error(() => _uplink.Data.QueryAsync<Profile>("Missing", "key"));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.NotFound));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("table_not_found"));
            Assert.That(_uplink.PendingDataQueryCount, Is.Zero);
            Assert.That(Frames("data_query"), Is.Empty);
        });

        [UnityTest] public IEnumerator UpsertSnapshotRetainsExactDecimalValue() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            await _uplink.Data.SendUpsertAsync("Profiles", "key", new ExactValue { Amount = 1234567890.1234567890123456789m });
            Assert.That(Frames("data_write").Single(), Does.Contain("\"amount\":1234567890.1234567890123456789"));
        });

        [UnityTest] public IEnumerator QueryDeserializesExactDecimalValueWithoutDoubleRoundTrip() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var query = _uplink.Data.QueryAsync<ExactValue>("Profiles", "key");
            _socket.Push("{\"type\":\"data_result\",\"request_id\":\"" + QueryIds().Single() +
                "\",\"entity\":\"Profiles\",\"id\":\"key\",\"found\":true,\"data\":{\"amount\":1234567890.1234567890123456789}}");
            Assert.That((await query).Value.Amount, Is.EqualTo(1234567890.1234567890123456789m));
        });

        [UnityTest] public IEnumerator PreUplinkRecordsCatalogueCannotOverrideSessionCatalogue() => H.Run(async () =>
        {
            _http.Handler = (_, __) => Task.FromResult(Catalogue(singleton: true));
            await PlayServGameServer.GetTablesAsync();
            _http.Handler = (_, __) => Task.FromResult(Catalogue(singleton: false));
            await _uplink.ConnectAsync();
            await _uplink.Data.SendDeleteAsync("Profiles", "key");
            Assert.That(_http.Requests.Count, Is.EqualTo(2));
            Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("session-one"));
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator ReconnectRechecksSingletonMetadataWithoutReconfiguration() => H.Run(async () =>
        {
            _http.Handler = (_, __) => Task.FromResult(Catalogue(singleton: true));
            await _uplink.ConnectAsync();
            await _uplink.Data.SendUpsertAsync("Profiles", "key", new Profile());
            await _uplink.DisconnectAsync();
            _socket = new H.Socket(H.Ack());
            _controlled = new ControlledSocket(_socket);
            _http.Handler = (_, __) => Task.FromResult(Catalogue(singleton: false));
            await _uplink.ConnectAsync();
            await _uplink.Data.SendDeleteAsync("Profiles", "key");
            Assert.That(_http.Requests.Count, Is.EqualTo(2));
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
            await _uplink.DisconnectAsync();
            _socket = new H.Socket(H.Ack());
            _controlled = new ControlledSocket(_socket);
            _http.Handler = (_, __) => Task.FromResult(Catalogue(singleton: true));
            await _uplink.ConnectAsync();
            try { await _uplink.Data.SendDeleteAsync("Profiles", "key"); Assert.Fail("Singleton delete must be refused."); }
            catch (InvalidOperationException) { }
            Assert.That(_http.Requests.Count, Is.EqualTo(3));
            Assert.That(Frames("data_write"), Is.Empty);
        });

        [UnityTest] public IEnumerator SingletonQueryAndUpsertPreserveContractEmptyKey() => H.Run(async () =>
        {
            _http.Handler = (_, __) => Task.FromResult(Catalogue(singleton: true));
            await _uplink.ConnectAsync();
            var query = Query("");
            Reply(QueryIds().Single(), "");
            Assert.That((await query).Key, Is.Empty);
            await _uplink.Data.SendUpsertAsync("Profiles", "", new Profile { Name = "singleton" });
            Assert.That(GameServerServiceValues.Object(Frames("data_write").Single())["id"], Is.EqualTo(""));
        });

        [UnityTest] public IEnumerator EmptyKeyForOrdinaryTableIsRejectedBeforeFrameSend() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            try { await Query(""); Assert.Fail(); } catch (ArgumentException) { }
            try { await _uplink.Data.SendUpsertAsync("Profiles", "", new Profile()); Assert.Fail(); } catch (ArgumentException) { }
            Assert.That(Frames("data_write"), Is.Empty);
            Assert.That(Frames("data_query"), Is.Empty);
        });

        private Task<PlayServUplinkDataResult<Profile>> Query(string key = "key", CancellationToken ct = default) =>
            _uplink.Data.QueryAsync<Profile>("Profiles", key, ct);

        private string[] QueryIds() => Frames("data_query").Select(frame => (string)GameServerServiceValues.Object(frame)["request_id"]).ToArray();
        private void Reply(string id, string key, bool found = true) => _socket.Push(
            "{\"type\":\"data_result\",\"request_id\":\"" + id + "\",\"entity\":\"Profiles\",\"id\":\"" + key +
            "\",\"found\":" + (found ? "true" : "false") + ",\"data\":{}}");

        private string[] Frames(string type) => _socket.Sent.Where(frame => frame.Contains("\"type\":\"" + type + "\"")).ToArray();
        private void AssertCatalogueOnly()
        {
            Assert.That(_http.Requests, Is.Not.Empty);
            Assert.That(_http.Requests.All(request => request.Method == "GET" && request.RelativePath == "data/tables" &&
                request.ServerKey == "session-one"), Is.True);
        }
        private static PlayServGameServerHttpResponse Catalogue(string read = "true", string write = "true", bool singleton = false) => new PlayServGameServerHttpResponse
        {
            StatusCode = 200,
            Body = "{\"data\":[{\"entity_id\":\"ent_profiles\",\"name\":\"Profiles\",\"singleton\":" + (singleton ? "true" : "false") +
                ",\"acl\":{\"client\":{\"read\":false,\"write\":false},\"server\":{\"read\":" + read + ",\"write\":" + write + "}}}]}"
        };

        public sealed class Profile
        {
            [PlayServJsonName("display_name")] public string Name { get; set; }
        }

        public sealed class ExactValue
        {
            [PlayServJsonName("amount")] public decimal Amount { get; set; }
        }

        private sealed class ControlledSocket : IPlayServUplinkSocket
        {
            private readonly H.Socket _inner;
            internal Exception SendError;
            internal Task SendGate;
            internal ControlledSocket(H.Socket inner) { _inner = inner; }
            public Task ConnectAsync(Uri endpoint, string credential, CancellationToken ct) => _inner.ConnectAsync(endpoint, credential, ct);
            public Task<string> ReceiveAsync(CancellationToken ct) => _inner.ReceiveAsync(ct);
            public Task SendAsync(byte[] bytes, CancellationToken ct)
            {
                var isData = Encoding.UTF8.GetString(bytes).Contains("\"type\":\"data_");
                if (isData && SendError != null) throw SendError;
                var sent = _inner.SendAsync(bytes, ct);
                return isData && SendGate != null ? SendGate : sent;
            }
            public void Abort() => _inner.Abort();
            public void Dispose() => _inner.Dispose();
        }
    }
}
