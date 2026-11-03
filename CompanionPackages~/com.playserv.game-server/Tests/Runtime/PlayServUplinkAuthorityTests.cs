using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServUplinkAuthorityTests
    {
        private PlayServGameServerUplink _uplink;
        private H.Socket _socket;
        private H.Http _http;
        internal const string Authority = "{\"enabled\":true,\"capability\":\"authoritative_state_v1\",\"epoch\":\"epoch-one\",\"retry_session\":\"retry-secret\",\"retry_window_ms\":60000}";

        [SetUp] public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown();
            _socket = new H.Socket(Ack(Authority));
            _http = new H.Http { Handler = (_, __) => Task.FromResult(new PlayServGameServerHttpResponse
            { StatusCode = 200, Body = "{\"data\":[{\"entity_id\":\"ent_profiles\",\"name\":\"Profiles\",\"singleton\":false,\"acl\":{\"server\":{\"read\":true,\"write\":true}}}]}" }) };
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(), HttpTimeout = TimeSpan.FromSeconds(2) },
                _http, () => DateTimeOffset.UtcNow, (delay, ct) => Task.Delay(delay, ct));
            _uplink = PlayServGameServer.Uplink;
            _uplink.SocketFactory = () => _socket;
        }

        [UnityTearDown] public IEnumerator Cleanup() => H.Run(async () =>
        {
            try { await _uplink.DisconnectAsync(); }
            finally { PlayServGameServer.CancelForModuleShutdown(); }
        });

        [UnityTest] public IEnumerator HelloOffersAuthorityCapability() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            Assert.That(_socket.Sent.First(), Does.Contain("authoritative_state_v1"));
        });

        [UnityTest] public IEnumerator MissingAuthorityDoesNotSilentlyDowngrade() => H.Run(async () =>
        {
            _socket = new H.Socket(Ack(null));
            var error = await H.Error(() => _uplink.ConnectAsync());
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("uplink_invalid_authority"));
        });

        [UnityTest] public IEnumerator GovernedSendWaitsForReceiptAndCarriesStableEnvelope() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var write = _uplink.Data.SendUpsertAsync("Profiles", "one", new { Score = 1 });
            await H.Until(() => Frames("data_write").Length != 0);
            await Ping();
            Assert.That(write.IsCompleted, Is.False, "A send is not an authority acceptance.");
            var frame = GameServerServiceValues.Object(Frames("data_write").Single());
            Assert.That(frame.ContainsKey("authority"), Is.True);
            var authority = (IDictionary<string, object>)frame["authority"];
            Assert.That(authority["epoch"], Is.EqualTo("epoch-one"));
            Assert.That(Guid.TryParse((string)authority["operation_id"], out _), Is.True);
            _socket.Push(Receipt(frame));
            await write;
        });

        [Test] public void DataOffersDisposableSubscriptionsAndOutcomeMethods()
        {
            var methods = typeof(PlayServUplinkData).GetMethods().Select(method => method.Name).ToArray();
            Assert.That(methods, Does.Contain("SubscribeAsync"));
            Assert.That(methods, Does.Contain("UpsertWithOutcomeAsync"));
            Assert.That(methods, Does.Contain("IncrementWithOutcomeAsync"));
            Assert.That(methods, Does.Contain("DeleteWithOutcomeAsync"));
        }

        [UnityTest] public IEnumerator EveryRefusalPreservesNumericCodeWithoutPayload() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            foreach (var code in new[] { 32001, 32002, 32003, 32101, 32102, 32103, 32104, 32105, 32106, 32107, 32108, 32109, 32110, 32111, 32112, 32113, 32114 })
            {
                var count = Frames("data_write").Length;
                var write = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one");
                await H.Until(() => Frames("data_write").Length > count);
                var frame = Object(Frames("data_write").Last());
                var authority = (IDictionary<string, object>)frame["authority"];
                _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_write_result", request_id = frame["request_id"], entity = "Profiles", id = "one",
                    authority = new { epoch = "epoch-one", operation_id = authority["operation_id"], outcome = "rejected", error_code = code, rejection = "safe", detail = "secret-payload sk_secret retry-secret" } }));
                var result = await write;
                Assert.That(result.State, Is.EqualTo(PlayServUplinkWriteState.Rejected));
                Assert.That(result.Error.TransportCode, Is.EqualTo(code));
                Assert.That(result.Error.Message + result.Error.RawDetails, Does.Not.Contain("secret"));
            }
        });

        [UnityTest] public IEnumerator SnapshotAndOriginalOperationSurviveCancellationAndResolve() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var cancel = new CancellationTokenSource();
            var input = new Dictionary<string, object> { ["Score"] = 1 };
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", input, 7, cancel.Token);
            input["Score"] = 9;
            await H.Until(() => Frames("data_write").Length == 1);
            var first = Object(Frames("data_write").Single());
            cancel.Cancel();
            var unknown = await write;
            Assert.That(unknown.State, Is.EqualTo(PlayServUplinkWriteState.Unknown));
            var resolve = unknown.ResolveAsync();
            await H.Until(() => Frames("authority_status_request").Length == 1);
            var status = Object(Frames("authority_status_request").Single());
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "authority_status", request_id = status["request_id"], known = false }));
            await H.Until(() => Frames("data_write").Length == 2);
            var second = Object(Frames("data_write").Last());
            Assert.That(PlayServGameServerJson.Serialize(second["authority"]), Is.EqualTo(PlayServGameServerJson.Serialize(first["authority"])));
            Assert.That(PlayServGameServerJson.Serialize(second["data"]), Is.EqualTo("{\"Score\":1}"));
            Assert.That(second["request_id"], Is.Not.EqualTo(first["request_id"]));
            _socket.Push(Receipt(second));
            Assert.That((await resolve).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
        });

        [UnityTest] public IEnumerator UnknownEarlierWriteIsNotRejectedByLaterUnrecordedCapacity() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var cancel = new CancellationTokenSource();
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", new { Score = 1 }, ct: cancel.Token);
            await H.Until(() => Frames("data_write").Length == 1);
            cancel.Cancel();
            var unknown = await write;
            var resolve = unknown.ResolveAsync();
            await H.Until(() => Frames("authority_status_request").Length == 1);
            var status = Object(Frames("authority_status_request").Single());
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "authority_status", request_id = status["request_id"], known = false }));
            await H.Until(() => Frames("data_write").Length == 2);
            var frame = Object(Frames("data_write").Last());
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_write_result", request_id = frame["request_id"], entity = "Profiles", id = "one",
                authority = new { epoch = "epoch-one", operation_id = ((IDictionary<string, object>)frame["authority"])["operation_id"], outcome = "rejected", error_code = 32106, rejection = "capacity" } }));
            Assert.That((await resolve).State, Is.EqualTo(PlayServUplinkWriteState.Unknown));
        });

        [UnityTest] public IEnumerator GovernedQueryRejectsMissingAuthorityStamp() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var query = _uplink.Data.QueryAsync<Dictionary<string, object>>("Profiles", "one");
            await H.Until(() => Frames("data_query").Length == 1);
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_result", request_id = Object(Frames("data_query").Single())["request_id"], entity = "Profiles", id = "one", found = true, data = new { Score = 1 } }));
            Assert.That((await H.Error(() => query)).UnifiedError.SourceCode, Is.EqualTo("invalid_response"));
        });

        [UnityTest] public IEnumerator FreshSubscriptionAlwaysRequestsFullStateEvenAfterPrime() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var opening = _uplink.Data.SubscribeAsync<Score>("Profiles");
            await H.Until(() => Frames("subscribe_data").Length == 1);
            PushRow("one", 1, 1, 1);
            AckRoute();
            await H.Until(() => Frames("data_changes").Length == 1);
            var request = Object(Frames("data_changes").Single());
            Assert.That(request["epoch"], Is.EqualTo(""), "A prime does not establish complete table membership.");
            Changes(request, true, 1, new[] { Row("one", 1, 1) });
            using var subscription = await opening;
            Assert.That(subscription.IsComplete, Is.True);
        });

        [UnityTest] public IEnumerator SharedRoutesRefCountAndOrderRowsRatherThanGlobalRevision() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var first = await OpenRoute();
            using var second = await _uplink.Data.SubscribeAsync<Score>("Profiles");
            Assert.That(Frames("subscribe_data").Length, Is.EqualTo(1));
            PushRow("two", 12, 10, 12);
            PushRow("one", 11, 12, 11);
            PushRow("one", 10, 12, -1);
            await Ping();
            Assert.That(first.Snapshot["one"].Value, Is.EqualTo(11));
            Assert.That(first.Snapshot["two"].Value, Is.EqualTo(12));
            Assert.That(first.Through, Is.EqualTo(12));
            try { await _uplink.Data.SubscribeAsync<Score>("Profiles", "field:Name"); Assert.Fail("Conflicting routes must fail locally."); }
            catch (InvalidOperationException) { }
            first.Dispose();
            Assert.That(Frames("unsubscribe_data"), Is.Empty);
            try { await first.RefreshAsync(); Assert.Fail("Disposed handles cannot refresh a shared route."); }
            catch (ObjectDisposedException) { }
            second.Dispose();
            await H.Until(() => Frames("unsubscribe_data").Length == 1);
        });

        [UnityTest] public IEnumerator ReconnectRequestsDeltaFromOldThroughNotNewAck() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            PushRow("one", 2, 2, 2); await Ping();
            var old = _socket;
            _socket = new H.Socket(Ack(Authority));
            old.Fail(new InvalidOperationException("connection lost"));
            await H.Until(() => Frames("subscribe_data").Length == 1);
            AckRoute(through: 100);
            await H.Until(() => Frames("data_changes").Length == 1);
            var request = Object(Frames("data_changes").Single());
            Assert.That(request["epoch"], Is.EqualTo("epoch-one"));
            Assert.That(request["after"], Is.EqualTo("2"));
            Changes(request, false, 4, new[] { Row("one", 4, 4), Row("two", 3, 0, true) });
            await H.Until(() => subscription.IsComplete);
            Assert.That(subscription.Snapshot["one"].Value, Is.EqualTo(4));
            Assert.That(subscription.Through, Is.EqualTo(4));
        });

        [UnityTest] public IEnumerator FullBaselineRetainsLiveRowBeforeFirstPage() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var opening = _uplink.Data.SubscribeAsync<Score>("Profiles");
            await H.Until(() => Frames("subscribe_data").Length == 1);
            AckRoute();
            await H.Until(() => Frames("data_changes").Length == 1);
            PushRow("live", 12, 10, 12);
            await Ping();
            Changes(Object(Frames("data_changes").Single()), true, 10, new[] { Row("one", 1, 1) });
            using var subscription = await opening;
            Assert.That(subscription.Snapshot.ContainsKey("live"), Is.True);
        });

        [UnityTest] public IEnumerator SubscriptionErrorsAreTypedAndDoNotLeakFrames() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var opening = _uplink.Data.SubscribeAsync<Score>("Profiles");
            await H.Until(() => Frames("subscribe_data").Length == 1);
            _socket.Push("{\"type\":\"subscribe_data_ack\",\"entity\":\"Profiles\",\"error\":\"access_denied\",\"error_code\":32109,\"detail\":\"sk_secret\"}");
            var error = await H.Error(() => opening);
            Assert.That(error.UnifiedError.TransportCode, Is.EqualTo(32109));
            Assert.That(error.ToString(), Does.Not.Contain("sk_secret"));
            Assert.That(Frames("data_changes"), Is.Empty);
        });

        [UnityTest] public IEnumerator AcceptedOwnWriteUpdatesSubscriptionBeforeAwaitCompletes() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "business-key", new { Score = 500 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            var accepted = await write;
            Assert.That(accepted.State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.Snapshot["one"].Value, Is.EqualTo(7));
            Assert.That(subscription.Snapshot.ContainsKey("business-key"), Is.False);
        });

        [UnityTest] public IEnumerator UngovernedSubscriptionsAreObservedOnlyAndDoNotRequestDelta() => H.Run(async () =>
        {
            _socket = new H.Socket(Ack("{\"enabled\":false,\"reason\":\"not_enabled\"}"));
            await _uplink.ConnectAsync();
            using var subscription = await _uplink.Data.SubscribeAsync<Score>("Profiles");
            Assert.That(subscription.IsComplete, Is.False);
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Observed));
            Assert.That(Frames("data_changes"), Is.Empty);
            _socket.Push("{\"type\":\"data_update\",\"entity\":\"Profiles\",\"id\":\"one\",\"op\":\"upsert\",\"data\":{\"Score\":3},\"version\":100}");
            await Ping();
            Assert.That(subscription.Snapshot["one"].Value, Is.EqualTo(3));
        });

        [UnityTest] public IEnumerator MalformedLiveWatermarkCannotLeaveRouteComplete() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            _socket.Push("{\"type\":\"data_update\",\"entity\":\"Profiles\",\"id\":\"one\",\"op\":\"upsert\",\"data\":{\"Score\":9},\"authority\":{\"epoch\":\"epoch-one\",\"revision\":\"2\"}}");
            await Ping();
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Faulted));
            Assert.That(subscription.Snapshot["one"].Value, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator GovernedQueryRejectsAnotherCorrelatedFrameType() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var query = _uplink.Data.QueryAsync<Score>("Profiles", "one");
            await H.Until(() => Frames("data_query").Length == 1);
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_changes_result", request_id = Object(Frames("data_query").Single())["request_id"], entity = "Profiles", id = "one", found = true,
                data = new { Score = 1 }, authority = new { epoch = "epoch-one", revision = "1" } }));
            Assert.That((await H.Error(() => query)).UnifiedError.SourceCode, Is.EqualTo("invalid_response"));
        });

        [UnityTest] public IEnumerator QueryPreservesInt64RevisionAndKnownEpoch() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var query = _uplink.Data.QueryAsync<Score>("Profiles", "one");
            await H.Until(() => Frames("data_query").Length == 1);
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_result", request_id = Object(Frames("data_query").Single())["request_id"], entity = "Profiles", id = "one", found = true,
                data = new { Score = 9 }, authority = new { epoch = "epoch-one", revision = "9007199254740993" } }));
            var result = await query;
            Assert.That(result.Revision, Is.EqualTo(9007199254740993L));
            Assert.That(result.Epoch, Is.EqualTo("epoch-one"));
            Assert.That(result.Value.Value, Is.EqualTo(9));
        });

        [UnityTest] public IEnumerator PaginatedRecoveryDoesNotCompleteEarlyOrResurrectDeletedRows() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            var first = Object(Frames("data_changes").Last());
            Changes(first, false, 2, new[] { Row("one", 2, 2) }, cursor: "page-two");
            await H.Until(() => Frames("data_changes").Length == 3);
            Assert.That(subscription.IsComplete, Is.False);
            PushRow("one", 4, 3, 0, true); await Ping();
            var second = Object(Frames("data_changes").Last());
            Assert.That(second["cursor"], Is.EqualTo("page-two"));
            Assert.That(second["after"], Is.EqualTo("1"));
            Changes(second, false, 3, new[] { Row("one", 3, 3), Row("two", 2, 2) });
            await refresh;
            Assert.That(subscription.Snapshot.ContainsKey("one"), Is.False);
            Assert.That(subscription.Snapshot["two"].Value, Is.EqualTo(2));
            Assert.That(subscription.Through, Is.EqualTo(3), "Highest row revision is not a safe table cursor.");
        });

        [UnityTest] public IEnumerator NewEpochReplacesMembershipAndRetiresLateOldFrames() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            PushRow("two", 2, 2, 2, epoch: "epoch-two");
            await H.Until(() => Frames("data_changes").Length == 2);
            var request = Object(Frames("data_changes").Last());
            Assert.That(request["epoch"], Is.EqualTo(""));
            Changes(request, true, 2, new[] { Row("two", 2, 2, epoch: "epoch-two") }, epoch: "epoch-two");
            await H.Until(() => subscription.IsComplete);
            PushRow("one", 100, 100, 100); await Ping();
            Assert.That(subscription.Epoch, Is.EqualTo("epoch-two"));
            Assert.That(subscription.Snapshot.Keys, Is.EquivalentTo(new[] { "two" }));
        });

        [UnityTest] public IEnumerator ShortSessionIsRefreshedOnceBeforeConcurrentWrites() => H.Run(async () =>
        {
            _socket = new H.Socket(Ack(Authority.Replace("60000", "4000")));
            await _uplink.ConnectAsync();
            var first = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one");
            var second = _uplink.Data.IncrementWithOutcomeAsync("Profiles", "two", new Dictionary<string, long> { ["Score"] = long.MaxValue });
            await H.Until(() => Frames("authority_context_request").Length == 1);
            Assert.That(Frames("data_write"), Is.Empty);
            var request = Object(Frames("authority_context_request").Single());
            var context = Object(Authority.Replace("retry-secret", "refreshed-secret")); context.Remove("enabled");
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "authority_context", request_id = request["request_id"], authority = context }));
            await H.Until(() => Frames("data_write").Length == 2);
            foreach (var frame in Frames("data_write").Select(Object))
            {
                Assert.That(((IDictionary<string, object>)frame["authority"])["retry_session"], Is.EqualTo("refreshed-secret"));
                Assert.That(frame.ContainsKey("project_id") || frame.ContainsKey("client_key") || frame.ContainsKey("environment"), Is.False);
                _socket.Push(Receipt(frame));
            }
            Assert.That((await first).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That((await second).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(Frames("authority_context_request").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator PendingHasBoundedIdenticalRetriesAndRemainsResolvable() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var write = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one");
            string identity = null;
            for (var i = 0; i < 4; i++)
            {
                var expected = i + 1;
                await H.Until(() => Frames("data_write").Length == expected);
                var frame = Object(Frames("data_write").Last());
                var operation = (string)((IDictionary<string, object>)frame["authority"])["operation_id"];
                if (identity == null) identity = operation; else Assert.That(operation, Is.EqualTo(identity));
                _socket.Push(Receipt(frame, "pending"));
            }
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Pending));
            Assert.That(Frames("data_write").Length, Is.EqualTo(4));
        });

        [UnityTest] public IEnumerator PreCanceledOperationDoesNotSendOrReadCatalogue() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one", ct: canceled.Token));
            Assert.That(Frames("data_write"), Is.Empty);
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator CancellationOfSharedOpeningDoesNotCancelOtherHandle() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var canceled = new CancellationTokenSource();
            var first = _uplink.Data.SubscribeAsync<Score>("Profiles", ct: canceled.Token);
            var second = _uplink.Data.SubscribeAsync<Score>("Profiles");
            await H.Until(() => Frames("subscribe_data").Length == 1);
            canceled.Cancel();
            try { await first; Assert.Fail(); } catch (OperationCanceledException) { }
            AckRoute();
            await H.Until(() => Frames("data_changes").Length == 1);
            Changes(Object(Frames("data_changes").Single()), true, 1, new[] { Row("one", 1, 1) });
            using var remaining = await second;
            Assert.That(remaining.IsComplete, Is.True);
            Assert.That(Frames("unsubscribe_data"), Is.Empty);
        });

        [UnityTest] public IEnumerator DisposedHandleDropsAlreadyQueuedCallbacks() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var delivery = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = delivery.Enqueue;
            var calls = 0;
            subscription.Changed += _ => calls++;
            PushRow("one", 2, 2, 2); await Ping();
            Assert.That(delivery.Count, Is.GreaterThan(0));
            subscription.Dispose();
            while (delivery.TryDequeue(out var callback)) callback();
            Assert.That(calls, Is.Zero);
        });

        [UnityTest] public IEnumerator SubscriptionCallbacksRunOutsideUplinkLock() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var sync = typeof(PlayServGameServerUplink).GetField("_sync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(_uplink);
            var delivered = new TaskCompletionSource<bool>();
            subscription.Changed += _ => delivered.TrySetResult(Monitor.IsEntered(sync));
            PushRow("one", 2, 2, 2);
            await H.Until(() => delivered.Task.IsCompleted);
            Assert.That(await delivered.Task, Is.False);
        });

        [UnityTest] public IEnumerator PreAuthorityRefusalKeepsCodeWithoutInventingReceiptIdentity() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            foreach (var code in new[] { 32002, 32003, 32109 })
            {
                var count = Frames("data_write").Length;
                var write = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one");
                await H.Until(() => Frames("data_write").Length > count);
                var frame = Object(Frames("data_write").Last());
                _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_write_result", request_id = frame["request_id"], entity = "Profiles", id = "one",
                    authority = new { outcome = "rejected", rejection = "access_denied", error_code = code, detail = "secret-payload" } }));
                var refusal = await write;
                Assert.That(refusal.State, Is.EqualTo(PlayServUplinkWriteState.Rejected));
                Assert.That(refusal.Error.TransportCode, Is.EqualTo(code));
            }
        });

        [UnityTest] public IEnumerator ExactlyOneMiBGovernedFrameIsAllowedWithoutPaddingReservation() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var empty = new { type = "data_write", request_id = new string('0', 32), entity = "Profiles", id = "one", op = "upsert", data = new { Text = "" },
                authority = new { epoch = "epoch-one", retry_session = "retry-secret", operation_id = Guid.Empty.ToString("D") } };
            var overhead = System.Text.Encoding.UTF8.GetByteCount(PlayServGameServerJson.Serialize(empty));
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", new { Text = new string('x', PlayServUplinkSocket.MaxFrameBytes - overhead) });
            await H.Until(() => Frames("data_write").Length == 1);
            var json = Frames("data_write").Single();
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.EqualTo(PlayServUplinkSocket.MaxFrameBytes));
            _socket.Push(Receipt(Object(json)));
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
        });

        [Test] public void OversizeFrameKeepsFrameTooLargeTypedError()
        {
            var error = Assert.Throws<PlayServGameServerException>(() => PlayServGameServerUplink.DataFrame(new { text = new string('x', PlayServUplinkSocket.MaxFrameBytes) }));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
        }

        [UnityTest] public IEnumerator FieldReceiptUsesServerKeyAndDeleteUsesBoundedCatchUp() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("field:Name");
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "request-key", new { Name = "ignored", Score = 99 });
            await H.Until(() => Frames("data_write").Length == 1);
            var frame = Object(Frames("data_write").Single());
            _socket.Push(ReceiptData(frame, new { Name = "server-key", Score = 7 }));
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.Snapshot["server-key"].Value, Is.EqualTo(7));
            Assert.That(subscription.Snapshot.ContainsKey("request-key"), Is.False);
            var deletion = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "request-key");
            await H.Until(() => Frames("data_write").Length == 2);
            _socket.Push(ReceiptData(Object(Frames("data_write").Last()), null, true, 3));
            await H.Until(() => Frames("data_changes").Length == 2);
            Assert.That(deletion.IsCompleted, Is.False);
            Changes(Object(Frames("data_changes").Last()), false, 3, new[] { Row("server-key", 3, 0, true) });
            Assert.That((await deletion).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.Snapshot.ContainsKey("server-key"), Is.False);
        });

        [UnityTest] public IEnumerator RelationReceiptCatchesUpInsteadOfInventingKey() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("relation:Owner->Name");
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "request-key", new { Score = 99 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            await H.Until(() => Frames("data_changes").Length == 2);
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("related-key", 2, 7) });
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.Snapshot["related-key"].Value, Is.EqualTo(7));
            Assert.That(subscription.Snapshot.ContainsKey("request-key"), Is.False);
        });

        [UnityTest] public IEnumerator LocalReceiptProjectionFailureDoesNotLoseAcceptedOutcome() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("field:Name");
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", new { Score = 2 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(ReceiptData(Object(Frames("data_write").Single()), new { Name = new string('x', 5000), Score = 2 }));
            var outcome = await write;
            Assert.That(outcome.State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Faulted));
            Assert.That((await outcome.ResolveAsync()).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(Frames("data_write").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator DisconnectRetiresSubscriptionIntentAndAmbiguousResolution() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var write = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one");
            await H.Until(() => Frames("data_write").Length == 1);
            await _uplink.DisconnectAsync();
            var outcome = await write;
            Assert.That(outcome.State, Is.EqualTo(PlayServUplinkWriteState.Unknown));
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Disposed));
            _socket = new H.Socket(Ack(Authority));
            await _uplink.ConnectAsync(); await Ping();
            Assert.That(Frames("subscribe_data"), Is.Empty);
            Assert.That((await outcome.ResolveAsync()).State, Is.EqualTo(PlayServUplinkWriteState.Unknown));
            Assert.That(Frames("authority_status_request"), Is.Empty);
        });

        [UnityTest] public IEnumerator SubscriptionCatalogueRefusalRemainsTypedAndSendsNoRoute() => H.Run(async () =>
        {
            _http.Handler = (_, __) => Task.FromResult(new PlayServGameServerHttpResponse { StatusCode = 200,
                Body = "{\"data\":[{\"entity_id\":\"ent_profiles\",\"name\":\"Profiles\",\"singleton\":false,\"acl\":{\"server\":{\"read\":false,\"write\":true}}}]}" });
            await _uplink.ConnectAsync();
            var error = await H.Error(() => _uplink.Data.SubscribeAsync<Score>("Profiles"));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(Playserv.Wrapper.PlayServErrorCode.Forbidden));
            Assert.That(Frames("subscribe_data"), Is.Empty);
        });

        [UnityTest] public IEnumerator TypedReceiptConversionNeverExposesPayloadInException() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", new { Score = 2 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(ReceiptData(Object(Frames("data_write").Single()), new { Score = "sk_secret" }));
            var outcome = await write;
            var error = Assert.Throws<PlayServGameServerException>(() => outcome.GetData<Score>());
            Assert.That(error.ToString(), Does.Not.Contain("sk_secret"));
        });

        [UnityTest] public IEnumerator ResumeCannotRestartRouteAlreadyBoundToCurrentConnection() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            typeof(PlayServGameServerUplink).GetMethod("ResumeDataSubscriptions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_uplink, null);
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Live));
            await Ping();
            Assert.That(Frames("subscribe_data").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator GovernedWriteCatalogueRefusalUsesCompanionErrorAndSendsNothing() => H.Run(async () =>
        {
            _http.Handler = (_, __) => Task.FromResult(new PlayServGameServerHttpResponse { StatusCode = 200,
                Body = "{\"data\":[{\"entity_id\":\"ent_profiles\",\"name\":\"Profiles\",\"singleton\":false,\"acl\":{\"server\":{\"read\":true,\"write\":false}}}]}" });
            await _uplink.ConnectAsync();
            var error = await H.Error(() => _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", new { Score = 2 }));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(Playserv.Wrapper.PlayServErrorCode.Forbidden));
            Assert.That(Frames("data_write"), Is.Empty);
        });

        [UnityTest] public IEnumerator DisconnectDuringReconnectRetiresAllRouteIntent() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var old = _socket;
            _socket = new H.Socket(null);
            old.Fail(new InvalidOperationException("connection lost"));
            await H.Until(() => Frames("uplink_hello").Length == 1);
            await _uplink.DisconnectAsync();
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Disposed));
            _socket = new H.Socket(Ack(Authority));
            await _uplink.ConnectAsync(); await Ping();
            Assert.That(Frames("subscribe_data"), Is.Empty);
        });

        [UnityTest] public IEnumerator GovernedPrimeAfterUngovernedReconnectReplacesVersionDomain() => H.Run(async () =>
        {
            _socket = new H.Socket(Ack("{\"enabled\":false,\"reason\":\"not_enabled\"}"));
            await _uplink.ConnectAsync();
            using var subscription = await _uplink.Data.SubscribeAsync<Score>("Profiles");
            _socket.Push("{\"type\":\"data_update\",\"entity\":\"Profiles\",\"id\":\"one\",\"op\":\"upsert\",\"data\":{\"Score\":99},\"version\":1700000000000}");
            await Ping();
            var old = _socket; _socket = new H.Socket(Ack(Authority)); old.Fail(new InvalidOperationException("connection lost"));
            await H.Until(() => Frames("subscribe_data").Length == 1);
            PushRow("one", 1, 1, 1); AckRoute();
            await H.Until(() => Frames("data_changes").Length == 1);
            Changes(Object(Frames("data_changes").Single()), true, 1, new[] { Row("one", 1, 1) });
            await H.Until(() => subscription.IsComplete);
            Assert.That(subscription.Snapshot["one"].Value, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator AcceptedOwnWriteBeforeFirstBaselineCannotBeLost() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var opening = _uplink.Data.SubscribeAsync<Score>("Profiles");
            await H.Until(() => Frames("subscribe_data").Length == 1); AckRoute();
            await H.Until(() => Frames("data_changes").Length == 1);
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", new { Score = 7 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Changes(Object(Frames("data_changes").Single()), true, 1, new[] { Row("one", 1, 1) });
            using var subscription = await opening;
            Assert.That(subscription.Snapshot["one"].Value, Is.EqualTo(7));
        });

        [UnityTest] public IEnumerator OwnUnmappedReceiptWaitsForExistingRecoveryThenCatchesUp() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("relation:Owner->Name");
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "one", new { Score = 7 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            await Ping();
            Changes(Object(Frames("data_changes").Last()), false, 1, Array.Empty<object>());
            await refresh;
            await H.Until(() => Frames("data_changes").Length == 3 || write.IsCompleted);
            Assert.That(write.IsCompleted, Is.False, "Own state needs catch-up after the older recovery.");
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("related", 2, 7) });
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.Snapshot["related"].Value, Is.EqualTo(7));
        });

        [UnityTest] public IEnumerator ConcurrentRefreshQueuesWithoutFaultingTheRoute() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var first = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            var second = subscription.RefreshAsync();
            await Ping();
            Assert.That(second.IsCompleted, Is.False);
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("one", 2, 2) });
            await first;
            await H.Until(() => Frames("data_changes").Length == 3);
            Assert.That(Object(Frames("data_changes").Last())["after"], Is.EqualTo("2"));
            Changes(Object(Frames("data_changes").Last()), false, 3, new[] { Row("one", 3, 3) });
            await second;
            Assert.That(subscription.IsComplete, Is.True);
        });

        [UnityTest] public IEnumerator NewEpochDuringExplicitRefreshRecoversAfterOlderPage() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            var older = Object(Frames("data_changes").Last());
            PushRow("two", 1, 1, 7, epoch: "epoch-two"); await Ping();
            Changes(older, false, 2, new[] { Row("one", 2, 2) });
            await H.Until(() => Frames("data_changes").Length == 3 || refresh.IsCompleted);
            Assert.That(Frames("data_changes").Length, Is.EqualTo(3), "A changed epoch needs a serialized full recovery.");
            var current = Object(Frames("data_changes").Last());
            Assert.That(current["epoch"], Is.EqualTo(""));
            Changes(current, true, 1, new[] { Row("two", 1, 7, epoch: "epoch-two") }, "epoch-two");
            await refresh;
            await H.Until(() => subscription.IsComplete);
            Assert.That(subscription.Snapshot.Keys, Is.EquivalentTo(new[] { "two" }));
        });

        [UnityTest] public IEnumerator SuccessfulRefreshReplacesFailedReconnectReadiness() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var old = _socket; _socket = new H.Socket(Ack(Authority)); old.Fail(new InvalidOperationException("connection lost"));
            await H.Until(() => Frames("subscribe_data").Length == 1);
            _socket.Push("{\"type\":\"subscribe_data_ack\",\"entity\":\"Profiles\",\"error\":{\"code\":32003}}");
            await H.Until(() => subscription.State == PlayServUplinkSubscriptionState.Faulted);
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 1);
            Changes(Object(Frames("data_changes").Single()), false, 2, new[] { Row("one", 2, 2) });
            await refresh;
            using var second = await _uplink.Data.SubscribeAsync<Score>("Profiles");
            Assert.That(second.IsComplete, Is.True);
            Assert.That(Frames("subscribe_data").Length, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator SnapshotPreservesIsoTextAsJsonString() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            var opening = _uplink.Data.SubscribeAsync<Dictionary<string, object>>("Profiles");
            await H.Until(() => Frames("subscribe_data").Length == 1); AckRoute();
            await H.Until(() => Frames("data_changes").Length == 1);
            const string iso = "2026-10-23T00:00:00Z";
            Changes(Object(Frames("data_changes").Single()), true, 1, new object[] {
                new { id = "one", op = "upsert", data = new { When = iso }, authority = new { epoch = "epoch-one", revision = "1" } } });
            using var subscription = await opening;
            Assert.That(subscription.Snapshot["one"]["When"], Is.TypeOf<string>().And.EqualTo(iso));
        });

        [UnityTest] public IEnumerator StalledUnityContextCoalescesRowsIntoOnePendingDispatch() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            await DataCallbacksIdle();
            var delivery = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = delivery.Enqueue;
            var changes = new List<int>();
            subscription.Changed += change => changes.Add(change.Value.Value);
            for (var revision = 2; revision <= 102; revision++) PushRow("one", revision, revision, revision);
            await Ping();
            await H.Until(() => delivery.Count > 0);
            Assert.That(delivery.Count, Is.EqualTo(1), "A stalled Unity dispatcher cannot retain one closure per update.");
            while (delivery.TryDequeue(out var callback)) callback();
            Assert.That(changes, Is.EqualTo(new[] { 102 }));
        });

        [UnityTest] public IEnumerator PreviousEpochFaultCannotBeDeliveredInNewEpoch() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            await DataCallbacksIdle();
            var delivery = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = delivery.Enqueue;
            var states = new List<PlayServUplinkSubscriptionState>();
            subscription.StateChanged += states.Add;
            _socket.Push("{\"type\":\"data_update\",\"entity\":\"Profiles\",\"id\":\"one\",\"op\":\"upsert\",\"data\":{},\"authority\":{\"epoch\":\"epoch-one\",\"revision\":\"2\"}}");
            await Ping();
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Faulted));
            PushRow("two", 1, 1, 7, epoch: "epoch-two"); await Ping();
            await H.Until(() => Frames("data_changes").Length == 2);
            while (delivery.TryDequeue(out var callback)) callback();
            Assert.That(states, Has.No.Member(PlayServUplinkSubscriptionState.Faulted));
            Changes(Object(Frames("data_changes").Last()), true, 1, new[] { Row("two", 1, 7, epoch: "epoch-two") }, "epoch-two");
            await H.Until(() => subscription.IsComplete);
        });

        [UnityTest] public IEnumerator LastCanceledOpeningCannotSendSubscribeAfterUnsubscribe() => H.Run(async () =>
        {
            var catalogue = new TaskCompletionSource<PlayServGameServerHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _http.Handler = (_, __) => catalogue.Task;
            await _uplink.ConnectAsync();
            using var cancellation = new CancellationTokenSource();
            var opening = _uplink.Data.SubscribeAsync<Score>("Profiles", ct: cancellation.Token);
            await H.Until(() => _http.Requests.Count > 0);
            cancellation.Cancel();
            try { await opening; Assert.Fail(); } catch (OperationCanceledException) { }
            await H.Until(() => Frames("unsubscribe_data").Length == 1);
            catalogue.SetResult(new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{\"data\":[]}" });
            await Ping();
            Assert.That(Frames("subscribe_data"), Is.Empty);
        });

        [UnityTest] public IEnumerator DisposedRoutesDoNotAccumulateBehindStalledUnityContext() => H.Run(async () =>
        {
            _socket = new H.Socket(Ack("{\"enabled\":false,\"reason\":\"not_enabled\"}"));
            await _uplink.ConnectAsync();
            using (await _uplink.Data.SubscribeAsync<Score>("Profiles")) { }
            var delivery = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = delivery.Enqueue;
            for (var index = 0; index < 140; index++)
                using (await _uplink.Data.SubscribeAsync<Score>("Profiles")) { }
            var field = typeof(PlayServGameServerUplink).GetField("_dataCallbackRoutes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var pending = (IEnumerable)field.GetValue(_uplink);
            Assert.That(pending.Cast<object>().Count(), Is.Zero, "Disposed handles must release their queued payloads before Unity resumes.");
        });

        [UnityTest] public IEnumerator CoalescedStateKeepsSafeErrorAcrossSameEpochRecovery() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            await DataCallbacksIdle();
            var delivery = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = delivery.Enqueue;
            var errors = new List<Playserv.Wrapper.PlayServError>();
            subscription.Failed += errors.Add;
            _socket.Push("{\"type\":\"data_update\",\"entity\":\"Profiles\",\"id\":\"one\",\"op\":\"upsert\",\"data\":{\"secret\":\"not-for-diagnostics\"},\"authority\":{\"epoch\":\"epoch-one\",\"revision\":\"2\"}}");
            await Ping();
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("one", 2, 2) });
            await refresh;
            while (delivery.TryDequeue(out var callback)) callback();
            Assert.That(subscription.IsComplete, Is.True);
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0].Message, Does.Not.Contain("not-for-diagnostics"));
        });

        [UnityTest] public IEnumerator CancellationOfQueuedRefreshDoesNotInvalidateActiveRecovery() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var first = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            using var cancellation = new CancellationTokenSource();
            var queued = subscription.RefreshAsync(cancellation.Token);
            cancellation.Cancel();
            try { await queued; Assert.Fail(); } catch (OperationCanceledException) { }
            Assert.That(subscription.State, Is.EqualTo(PlayServUplinkSubscriptionState.Recovering));
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("one", 2, 2) });
            await first;
            Assert.That(subscription.IsComplete, Is.True);
            Assert.That(Frames("data_changes").Length, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator OwnRelationReceiptCannotBeSkippedByLaterLiveThrough() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("relation:Owner->Name");
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "own", new { Score = 7 });
            await H.Until(() => Frames("data_write").Length == 1);
            PushRow("other", 3, 3, 3); await Ping();
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            await H.Until(() => Frames("data_changes").Length == 2);
            var changes = Object(Frames("data_changes").Last());
            Assert.That(changes["after"], Is.EqualTo("1"));
            Changes(changes, false, 2, new[] { Row("related", 2, 7) });
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.Snapshot["related"].Value, Is.EqualTo(7));
            Assert.That(subscription.Through, Is.EqualTo(3), "Recovery cannot move a safe checkpoint backwards.");
        });

        [UnityTest] public IEnumerator OwnFieldDeleteRecoveryUsesReceiptFloorAfterWaitingForGate() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("field:Owner");
            var write = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "own");
            await H.Until(() => Frames("data_write").Length == 1);
            PushRow("other", 3, 3, 3); await Ping();
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            var prior = Object(Frames("data_changes").Last());
            _socket.Push(ReceiptData(Object(Frames("data_write").Single()), null, deleted: true, revision: 2));
            await Ping();
            Changes(prior, false, 3, Array.Empty<object>()); await refresh;
            await H.Until(() => Frames("data_changes").Length == 3);
            var changes = Object(Frames("data_changes").Last());
            Assert.That(changes["after"], Is.EqualTo("1"));
            Changes(changes, false, 3, new[] { Row("one", 2, 0, deleted: true) });
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.Snapshot.ContainsKey("one"), Is.False);
            Assert.That(subscription.IsComplete, Is.True);
        });

        [UnityTest] public IEnumerator CancelledOwnReceiptRecoveryCannotLetOlderPageAdvertiseComplete() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("relation:Owner->Name");
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            var prior = Object(Frames("data_changes").Last());
            using var cancellation = new CancellationTokenSource();
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "own", new { Score = 7 }, ct: cancellation.Token);
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            await Ping();
            // Receipt processing queues behind the active recovery before cancellation.
            var queries = typeof(PlayServGameServerUplink).GetField("_dataQueries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            await H.Until(() => ((IDictionary)queries.GetValue(_uplink)).Count == 1);
            cancellation.Cancel(); await write;
            Changes(prior, false, 1, Array.Empty<object>()); await refresh;
            Assert.That(subscription.IsComplete, Is.False, "Known missing accepted state survives caller cancellation.");
            var repair = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 3);
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("related", 2, 7) });
            await repair;
            Assert.That(subscription.Snapshot["related"].Value, Is.EqualTo(7));
            Assert.That(subscription.IsComplete, Is.True);
        });

        [UnityTest] public IEnumerator NewEpochFullPageMidPaginationRestartsWithoutLiveSignal() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute();
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("one", 2, 2) }, cursor: "old-page");
            await H.Until(() => Frames("data_changes").Length == 3);
            Changes(Object(Frames("data_changes").Last()), true, 1, new[] { Row("two", 1, 7, epoch: "epoch-two") }, "epoch-two");
            await H.Until(() => Frames("data_changes").Length == 4 || refresh.IsCompleted);
            Assert.That(refresh.IsCompleted, Is.False);
            var reset = Object(Frames("data_changes").Last());
            Assert.That(reset["epoch"], Is.EqualTo(""));
            Assert.That(reset.ContainsKey("cursor"), Is.False);
            Changes(reset, true, 1, new[] { Row("two", 1, 7, epoch: "epoch-two") }, "epoch-two");
            await refresh;
            Assert.That(subscription.IsComplete, Is.True);
            Assert.That(subscription.Snapshot.Keys, Is.EquivalentTo(new[] { "two" }));
        });

        [UnityTest] public IEnumerator OldOwnReceiptRecoveryErrorCannotFaultReplacementEpoch() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("relation:Owner->Name");
            await DataCallbacksIdle();
            var errors = new System.Collections.Concurrent.ConcurrentQueue<Playserv.Wrapper.PlayServError>();
            subscription.Failed += errors.Enqueue;
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "own", new { Score = 7 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            await H.Until(() => Frames("data_changes").Length == 2);
            var old = Object(Frames("data_changes").Last());
            PushRow("new", 1, 1, 9, epoch: "epoch-two"); await Ping();
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_changes_result", request_id = old["request_id"],
                entity = "Profiles", error = new { code = 32003 } }));
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            await H.Until(() => Frames("data_changes").Length == 3);
            Changes(Object(Frames("data_changes").Last()), true, 1, new[] { Row("new", 1, 9, epoch: "epoch-two") }, "epoch-two");
            await H.Until(() => subscription.IsComplete);
            await DataCallbacksIdle();
            Assert.That(errors, Is.Empty, "An obsolete receipt's failure cannot belong to the new epoch.");
        });

        [UnityTest] public IEnumerator FailedOwnProjectionRemainsIncompleteAfterOlderRecoveryPage() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("field:Owner");
            var refresh = subscription.RefreshAsync();
            await H.Until(() => Frames("data_changes").Length == 2);
            var older = Object(Frames("data_changes").Last());
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "own", new { Score = 7 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(ReceiptData(Object(Frames("data_write").Single()), new { Score = 7, Owner = new string('x', 4097) }, revision: 2));
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            Assert.That(subscription.IsComplete, Is.False);
            Changes(older, false, 1, Array.Empty<object>()); await refresh;
            Assert.That(subscription.IsComplete, Is.False, "A failed accepted projection must survive completion of an older recovery.");
        });

        [UnityTest] public IEnumerator NewEpochFullPageDuringOwnReceiptStartsIndependentRecovery() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("relation:Owner->Name");
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "own", new { Score = 7 });
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            await H.Until(() => Frames("data_changes").Length == 2);
            Changes(Object(Frames("data_changes").Last()), false, 2, new[] { Row("related", 2, 7) }, cursor: "old-page");
            await H.Until(() => Frames("data_changes").Length == 3);
            Changes(Object(Frames("data_changes").Last()), true, 1, new[] { Row("new", 1, 9, epoch: "epoch-two") }, "epoch-two");
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            await H.Until(() => Frames("data_changes").Length == 4);
            var reset = Object(Frames("data_changes").Last());
            Assert.That(reset["epoch"], Is.EqualTo(""));
            Changes(reset, true, 1, new[] { Row("new", 1, 9, epoch: "epoch-two") }, "epoch-two");
            await H.Until(() => subscription.IsComplete);
            Assert.That(subscription.Snapshot.Keys, Is.EquivalentTo(new[] { "new" }));
        });

        [UnityTest] public IEnumerator FirstPageEpochChangeSurvivesOwnWriteCancellationDuringNextPage() => H.Run(async () =>
        {
            await _uplink.ConnectAsync();
            using var subscription = await OpenRoute("relation:Owner->Name");
            using var cancellation = new CancellationTokenSource();
            var write = _uplink.Data.UpsertWithOutcomeAsync("Profiles", "own", new { Score = 7 }, ct: cancellation.Token);
            await H.Until(() => Frames("data_write").Length == 1);
            _socket.Push(Receipt(Object(Frames("data_write").Single()), revision: "2", score: 7));
            await H.Until(() => Frames("data_changes").Length == 2);
            Changes(Object(Frames("data_changes").Last()), true, 1, new[] { Row("new", 1, 9, epoch: "epoch-two") }, "epoch-two", "new-page");
            await H.Until(() => Frames("data_changes").Length == 3);
            cancellation.Cancel();
            Assert.That((await write).State, Is.EqualTo(PlayServUplinkWriteState.Accepted));
            await H.Until(() => Frames("data_changes").Length == 4);
            var reset = Object(Frames("data_changes").Last());
            Assert.That(reset["epoch"], Is.EqualTo(""));
            Assert.That(reset.ContainsKey("cursor"), Is.False);
            Changes(reset, true, 1, new[] { Row("new", 1, 9, epoch: "epoch-two") }, "epoch-two");
            await H.Until(() => subscription.IsComplete);
            Assert.That(subscription.Snapshot.Keys, Is.EquivalentTo(new[] { "new" }));
        });

        private Task DataCallbacksIdle()
        {
            var field = typeof(PlayServGameServerUplink).GetField("_dispatchingDataCallbacks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return H.Until(() => !(bool)field.GetValue(_uplink));
        }

        private static string ReceiptData(IDictionary<string, object> frame, object data, bool deleted = false, long revision = 2) =>
            PlayServGameServerJson.Serialize(new { type = "data_write_result", request_id = frame["request_id"], entity = frame["entity"], id = frame["id"],
                authority = new { operation_id = ((IDictionary<string, object>)frame["authority"])["operation_id"], epoch = "epoch-one", outcome = "accepted",
                    revision = revision.ToString(), record_id = "stored-row", deleted, data } });

        private async Task<PlayServUplinkDataSubscription<Score>> OpenRoute(string keyPath = "self")
        {
            var opening = _uplink.Data.SubscribeAsync<Score>("Profiles", keyPath);
            await H.Until(() => Frames("subscribe_data").Length == 1);
            AckRoute();
            await H.Until(() => Frames("data_changes").Length == 1);
            Changes(Object(Frames("data_changes").Single()), true, 1, new[] { Row("one", 1, 1) });
            return await opening;
        }
        private void AckRoute(string epoch = "epoch-one", long through = 1) => _socket.Push(PlayServGameServerJson.Serialize(new
        { type = "subscribe_data_ack", entity = "Profiles", epoch, through = through.ToString() }));
        private void PushRow(string key, long revision, long through, int score, bool deleted = false, string epoch = "epoch-one") =>
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_update", entity = "Profiles", id = key,
                op = deleted ? "delete" : "upsert", data = new { Score = score }, authority = new { epoch, revision = revision.ToString(), through = through.ToString() } }));
        private static object Row(string key, long revision, int score, bool deleted = false, string epoch = "epoch-one") =>
            new { id = key, op = deleted ? "delete" : "upsert", data = new { Score = score }, authority = new { epoch, revision = revision.ToString() } };
        private void Changes(IDictionary<string, object> request, bool full, long through, object[] rows, string epoch = "epoch-one", string cursor = null) =>
            _socket.Push(PlayServGameServerJson.Serialize(new { type = "data_changes_result", request_id = request["request_id"], entity = "Profiles", epoch, full, rows, through = through.ToString(), cursor }));
        private static IDictionary<string, object> Object(string json) => GameServerServiceValues.Object(json);
        private sealed class Score { [Playserv.Serialization.PlayServJsonName("Score")] public int Value { get; set; } }

        internal static string Ack(string authority)
        {
            var ack = GameServerServiceValues.Object(H.Ack());
            ack.Remove("authority");
            if (authority != null) ack["authority"] = GameServerServiceValues.Object(authority);
            return PlayServGameServerJson.Serialize(ack);
        }
        private string[] Frames(string type) => _socket.Sent.Where(frame => frame.Contains("\"type\":\"" + type + "\"")).ToArray();
        private async Task Ping()
        {
            var count = Frames("pong").Length;
            _socket.Push("{\"type\":\"ping\"}");
            await H.Until(() => Frames("pong").Length > count);
        }
        internal static string Receipt(IDictionary<string, object> frame, string outcome = "accepted", string revision = "1", int score = 1)
        {
            var authority = (IDictionary<string, object>)frame["authority"];
            return PlayServGameServerJson.Serialize(new { type = "data_write_result", request_id = frame["request_id"], entity = frame["entity"], id = frame["id"],
                authority = new { operation_id = authority["operation_id"], epoch = authority["epoch"], outcome, revision, record_id = "one", deleted = false, data = new { Score = score } } });
        }
    }
}
