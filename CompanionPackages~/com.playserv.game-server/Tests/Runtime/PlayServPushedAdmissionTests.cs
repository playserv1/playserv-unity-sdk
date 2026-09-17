using System;
using System.Collections;
using System.Collections.Concurrent;
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
    public sealed class PlayServPushedAdmissionTests
    {
        private const string Player = "plr_01J8ZKQ4X0";
        private H.Http _http;
        private ConcurrentQueue<H.Socket> _sockets;
        private double _now;
        private ConcurrentQueue<PlayServRoomAdmissionResult> _rejected;
        private H.AdmissionTestScope _scope;
        private H.Socket Socket => _sockets.Last();
        private PlayServGameServerUplink Uplink => PlayServGameServer.Uplink;
        [SetUp] public void Setup() { _scope = new H.AdmissionTestScope(); _now = 100; Configure(); }
        [UnityTearDown] public IEnumerator Cleanup() => H.Run(() => _scope.CleanupAsync());
        private void Configure(Action<PlayServGameServerOptions> change = null, string admission = "push")
        {
            PlayServGameServer.CancelForModuleShutdown();
            _http = new H.Http(); _sockets = new ConcurrentQueue<H.Socket>();
            _rejected = new ConcurrentQueue<PlayServRoomAdmissionResult>();
            var options = new PlayServGameServerOptions { BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key() };
            change?.Invoke(options);
            PlayServGameServer.ConfigureForTesting(options, _http, () => DateTimeOffset.FromUnixTimeSeconds(1000), (_, ct) => Task.Delay(Timeout.Infinite, ct));
            PlayServGameServer.GetContextForServices().MonotonicSeconds = () => _now;
            Uplink.Seconds = () => _now;
            _scope.Track(Uplink);
            var sockets = _sockets;
            Uplink.SocketFactory = () =>
            {
                var ack = H.Ack().Replace("\"admission\":\"consume\",", admission == null ? "" : "\"admission\":\"" + admission + "\",");
                var socket = new H.Socket(ack); sockets.Enqueue(socket); return socket;
            };
        }
        private async Task<PlayServGameRoomHandle> Start(string name = "one")
        {
            var room = await PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest("arena", new PlayServGameRoomSnapshot(name, 0, 8)));
            _scope.Track(room);
            var rejections = _rejected;
            Action<PlayServRoomAdmissionResult> rejected = value => rejections.Enqueue(value);
            room.Admission.AdmissionRejected += rejected;
            _scope.UnsubscribeOnCleanup(() => room.Admission.AdmissionRejected -= rejected);
            return room;
        }
        private Task Until(Func<bool> predicate) => H.Until(predicate, () =>
            $"Uplink={Uplink.State}; sockets={_sockets.Count}; rejected={_rejected.Count}.");
        private async Task ReceiveBarrier()
        {
            var socket = Socket;
            var pongs = socket.Sent.Count(s => s == "{\"type\":\"pong\"}");
            socket.Push("{\"type\":\"ping\"}");
            await Until(() => socket.Sent.Count(s => s == "{\"type\":\"pong\"}") == pongs + 1);
        }
        private async Task Rejected(PlayServRoomAdmissionResult admission)
        {
            Assert.That(admission.Ok, Is.True, admission.Error.SourceCode);
            await Until(() => _rejected.Any(result => result.AdmissionId == admission.AdmissionId));
            Assert.That(_rejected.Count, Is.EqualTo(1), "Expected exactly one rejection for this configuration.");
        }
        internal static string Offer(string token = "rsv_test", string room = "one", string player = Player, string ttl = "10", string parameters = "null") =>
            "{\"type\":\"ticket_offer\",\"reservation_token\":\"" + token + "\",\"room_name\":\"" + room + "\",\"player_id\":\"" + player + "\",\"expires_in\":" + ttl + ",\"params\":" + parameters + "}";
        private async Task OfferAccepted(string token = "rsv_test", string room = "one", string player = Player)
        {
            Socket.Push(Offer(token, room, player));
            await Until(() => Socket.Sent.Any(s => s.Contains("\"ticket_result\"") && s.Contains(token) && s.Contains("\"ok\":true")));
        }
        private void Ack(string token = "rsv_test", bool ok = true, string room = "one", string player = Player, string reason = "reservation_expired") =>
            Socket.Push("{\"type\":\"join_ack\",\"reservation_token\":\"" + token + "\",\"room_name\":\"" + room + "\",\"player_id\":\"" + player + "\",\"ok\":" + (ok ? "true" : "false") + ",\"reason\":\"" + reason + "\"}");

        [UnityTest] public IEnumerator ConnectionTrackerParksAndReplacesOnlyTheOldAdmission() => H.Run(async () =>
        {
            var room = await Start(); var tracker = room.CreateConnectionTracker();
            var removed = new ConcurrentQueue<PlayServConnectionRemoval>(); tracker.ConnectionRemoved += removed.Enqueue;
            await OfferAccepted(); var first = tracker.TryAdmit("connection-one", "rsv_test");
            Assert.That(first.Ok, Is.True);
            Assert.That(tracker.TryGetPlayerId("connection-one", out var player), Is.True); Assert.That(player, Is.EqualTo(Player));
            await room.DrainPresenceForTesting();
            await OfferAccepted("rsv_second");
            Assert.That(tracker.TryAdmit("connection-two", "rsv_second").Ok, Is.False, "Cannot replace an active player.");
            tracker.ReportDisconnected("connection-one");
            var second = tracker.TryAdmit("connection-two", "rsv_second"); Assert.That(second.Ok, Is.True);
            Assert.That(tracker.TryAdmit("connection-two", "rsv_second").Ok, Is.False);
            await room.DrainPresenceForTesting(); Ack(ok: false); await ReceiveBarrier();
            tracker.ReportDisconnected("connection-one");
            Assert.That(tracker.TryGetPlayerId("connection-two", out _), Is.True); Assert.That(removed, Is.Empty);
            Ack("rsv_second"); await ReceiveBarrier();
            tracker.ReportDisconnected("connection-two"); _now += 29; tracker.Evaluate();
            Assert.That(room.RosterSnapshot(), Has.Length.EqualTo(1));
            tracker.ReportDisconnected("connection-two"); _now += 1; tracker.Evaluate();
            await room.DrainPresenceForTesting(); await Until(() => removed.Count == 1);
            Assert.That(room.RosterSnapshot(), Is.Empty);
            Assert.That(Socket.Sent.Count(s => s.Contains("\"event\":\"join\"")), Is.EqualTo(2));
            Assert.That(Socket.Sent.Count(s => s.Contains("\"event\":\"leave\"")), Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator TrackerKickAndShutdownNotifyExactConnection() => H.Run(async () =>
        {
            var room = await Start(); var tracker = room.CreateConnectionTracker(TimeSpan.Zero);
            var removed = new ConcurrentQueue<PlayServConnectionRemoval>(); tracker.ConnectionRemoved += removed.Enqueue;
            await OfferAccepted(); Assert.That(tracker.TryAdmit("one", "rsv_test").Ok, Is.True);
            await room.DrainPresenceForTesting(); Ack(); await ReceiveBarrier();
            Assert.That(tracker.RemovePlayer(Player), Is.True); Assert.That(tracker.RemovePlayer(Player), Is.False);
            await Until(() => removed.Count == 1);
            await OfferAccepted("rsv_second"); Assert.That(tracker.TryAdmit("two", "rsv_second").Ok, Is.True);
            await room.CloseAsync(); await Until(() => removed.Count == 2);
            Assert.That(tracker.TryGetPlayerId("two", out _), Is.False);
        });

        [UnityTest] public IEnumerator DefaultNegotiationAndLocalAdmissionDoNotWaitForAck() => H.Run(async () =>
        {
            var room = await Start();
            Assert.That(Socket.Sent.First(), Does.Contain("\"admission_push\""));
            Assert.That(Uplink.AdmissionMode, Is.EqualTo(PlayServAdmissionMode.Push));
            await OfferAccepted(); var httpCount = _http.Requests.Count;
            var result = room.Admission.TryAdmit("rsv_test");
            Assert.That(result.Ok, Is.True); Assert.That(result.PlayerId, Is.EqualTo(Player)); Assert.That(result.AdmissionId, Is.Not.Empty);
            Assert.That(room.RosterSnapshot(), Is.EqualTo(new[] { Player }));
            room.ReportJoin(Player); await room.DrainPresenceForTesting();
            Assert.That(Socket.Sent.Count(s => s.Contains("\"event\":\"join\"")), Is.EqualTo(1));
            Assert.That(Socket.Sent.Last(), Does.Contain("\"reservation_token\":\"rsv_test\""));
            Assert.That(_http.Requests.Count, Is.EqualTo(httpCount));
            Assert.That(room.Admission.TryAdmit("rsv_test").Error.SourceCode, Is.EqualTo("reservation_consumed"));
        });
        [UnityTest] public IEnumerator LegacyOptOutAndMissingAckFieldPreserveConsume() => H.Run(async () =>
        {
            Configure(o => o.EnablePushedAdmission = false, null);
            var room = await Start(); Assert.That(Socket.Sent.First(), Does.Not.Contain("admission_push"));
            Assert.That(Uplink.AdmissionMode, Is.EqualTo(PlayServAdmissionMode.Consume));
            room.ReportJoin(Player); await room.DrainPresenceForTesting();
            Assert.That(Socket.Sent.Last(), Does.Not.Contain("reservation_token"));
            Configure(admission: "consume"); await Uplink.ConnectAsync();
            Assert.That(Uplink.AdmissionMode, Is.EqualTo(PlayServAdmissionMode.Consume));
        });
        [UnityTest] public IEnumerator UnexpectedOrUnknownAdmissionIsTerminal() => H.Run(async () =>
        {
            foreach (var mode in new[] { "push", "unknown" })
            {
                Configure(o => o.EnablePushedAdmission = false, mode);
                var error = await H.Error(() => Uplink.ConnectAsync());
                Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("uplink_invalid_hello_ack"));
                Assert.That(Uplink.State, Is.EqualTo(PlayServUplinkState.Terminated));
            }
        });
        [UnityTest] public IEnumerator LegacyMutationsAreRefusedBeforeIoAndRosterChange() => H.Run(async () =>
        {
            var room = await Start(); var count = _http.Requests.Count;
            Assert.Throws<PlayServGameServerException>(() => room.ReportJoin(Player));
            var error = await H.Error(() => PlayServGameServer.ConsumeReservationAsync("arena", "rsv_test", Player, "one"));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("admission_mode_mismatch"));
            Assert.That(room.RosterSnapshot(), Is.Empty); Assert.That(_http.Requests.Count, Is.EqualTo(count));
        });
        [UnityTest] public IEnumerator HookGetsImmutableParamsOnUnityContextAndRefusesSafely() => H.Run(async () =>
        {
            var thread = Thread.CurrentThread.ManagedThreadId; var calls = 0; var actualThread = 0; object copy = null;
            Configure(o => o.TicketOfferHandler = (offer, ct) =>
            {
                actualThread = Thread.CurrentThread.ManagedThreadId; calls++;
                ((IDictionary<string, object>)offer.Params)["x"] = "changed";
                copy = ((IDictionary<string, object>)offer.Params)["x"];
                return Task.FromResult(PlayServTicketDecision.Refuse("room_refused", "sk_secret Bearer a.b.c rsv_test"));
            });
            var room = await Start(); var deliveries = new ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = action => deliveries.Enqueue(action);
            Socket.Push(Offer(parameters: "{\"x\":\"original\"}"));
            await Until(() =>
            {
                while (deliveries.TryDequeue(out var action)) action();
                return Socket.Sent.Any(s => s.Contains("ticket_result"));
            });
            var response = Socket.Sent.Last(); Assert.That(response, Does.Contain("room_refused"));
            Assert.That(actualThread, Is.EqualTo(thread)); Assert.That(copy, Is.EqualTo("original"));
            Assert.That(response, Does.Not.Contain("sk_secret").And.Not.Contain("a.b.c"));
            Socket.Push(Offer(parameters: "{\"x\":\"original\"}"));
            await Until(() => Socket.Sent.Count(s => s.Contains("ticket_result")) == 2);
            Assert.That(calls, Is.EqualTo(1)); Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.False);
        });
        [UnityTest] public IEnumerator SlowHookDoesNotBlockPingOrSendLateAnswer() => H.Run(async () =>
        {
            var source = new TaskCompletionSource<PlayServTicketDecision>(); var started = false; CancellationToken captured = default;
            Configure(o => o.TicketOfferHandler = (_, ct) => { started = true; captured = ct; return source.Task; });
            Uplink.OfferTimeout = TimeSpan.FromMilliseconds(100);
            await Start(); Socket.Push(Offer()); await Until(() => started);
            Socket.Push("{\"type\":\"ping\"}"); await Until(() => Socket.Sent.Any(s => s.Contains("pong")));
            await Until(() => captured.IsCancellationRequested);
            source.SetResult(PlayServTicketDecision.Accept()); await Task.Delay(30);
            Assert.That(Socket.Sent.Any(s => s.Contains("ticket_result")), Is.False);
        });
        [UnityTest] public IEnumerator MalformedOffersNeverCoerceTtlOrLeakPayload() => H.Run(async () =>
        {
            await Start();
            foreach (var ttl in new[] { "-1", "1.5", "\"10\"", "2147483648", "null", "true" })
            {
                var previous = Uplink.LastError;
                Socket.Push(Offer(ttl: ttl, parameters: "{\"secret\":\"sk_secret a.b.c\"}"));
                await Until(() => !ReferenceEquals(previous, Uplink.LastError));
                Assert.That(Uplink.LastError.SourceCode, Is.EqualTo("admission_invalid_frame"));
                Assert.That(Uplink.LastError.Message, Does.Not.Contain("sk_secret").And.Not.Contain("a.b.c"));
            }
            Assert.That(Socket.Sent.Any(s => s.Contains("ticket_result")), Is.False);
        });
        [UnityTest] public IEnumerator DuplicateCannotExtendTtlOrChangeIdentity() => H.Run(async () =>
        {
            var room = await Start(); await OfferAccepted();
            _now += 8; Socket.Push(Offer()); await Until(() => Socket.Sent.Count(s => s.Contains("ticket_result")) == 2);
            Socket.Push(Offer(player: "plr_other")); await Until(() => Socket.Sent.Any(s => s.Contains("reservation_invalid")));
            _now += 3;
            Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.False);
            await Uplink.SweepAdmissionsAsync();
            Assert.That(Uplink.RetainedAdmissionCount, Is.Zero);
            Assert.That(Socket.Sent.Any(s => s.Contains("ticket_release") && s.Contains("reservation_expired")), Is.True);
        });
        [UnityTest] public IEnumerator ConcurrentAdmissionsHaveOneWinnerAndNoDuplicateJoin() => H.Run(async () =>
        {
            var room = await Start(); await OfferAccepted();
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => room.Admission.TryAdmit("rsv_test"))));
            Assert.That(results.Count(r => r.Ok), Is.EqualTo(1)); await room.DrainPresenceForTesting();
            Assert.That(Socket.Sent.Count(s => s.Contains("\"event\":\"join\"")), Is.EqualTo(1));
        });
        [UnityTest] public IEnumerator NegativeAckRevokesExactAdmissionAndLateAckCannotKickReplacement() => H.Run(async () =>
        {
            var room = await Start(); await OfferAccepted(); var first = room.Admission.TryAdmit("rsv_test");
            Assert.That(first.Ok, Is.True);
            await room.DrainPresenceForTesting();
            Ack(ok: false, player: "plr_other"); await ReceiveBarrier(); Assert.That(_rejected, Is.Empty);
            Ack(ok: false); await Rejected(first);
            Assert.That(_rejected.Single().AdmissionId, Is.EqualTo(first.AdmissionId)); Assert.That(room.RosterSnapshot(), Is.Empty);
            await OfferAccepted("rsv_next"); Assert.That(room.Admission.TryAdmit("rsv_next").Ok, Is.True);
            Ack(ok: false); await ReceiveBarrier();
            Assert.That(_rejected.Count, Is.EqualTo(1)); Assert.That(room.RosterSnapshot(), Is.EqualTo(new[] { Player }));
        });
        [UnityTest] public IEnumerator MissingAckTimesOutAndDoesNotReplayJoin() => H.Run(async () =>
        {
            Configure(o => o.HttpTimeout = TimeSpan.FromSeconds(1)); var room = await Start(); await OfferAccepted();
            var admission = room.Admission.TryAdmit("rsv_test"); Assert.That(admission.Ok, Is.True);
            await room.DrainPresenceForTesting();
            _now += 2; await Uplink.SweepAdmissionsAsync(); await Rejected(admission);
            Assert.That(_rejected.Single().Error.SourceCode, Is.EqualTo("admission_ack_timeout"));
            Assert.That(room.RosterSnapshot(), Is.Empty); Assert.That(Socket.Sent.Count(s => s.Contains("\"event\":\"join\"")), Is.EqualTo(1));
        });
        [UnityTest] public IEnumerator DisconnectRejectsPendingButPreservesUnusedTicketUntilTtl() => H.Run(async () =>
        {
            var room = await Start(); await OfferAccepted(); await OfferAccepted("rsv_unused", player: "plr_other");
            var admission = room.Admission.TryAdmit("rsv_test"); Assert.That(admission.Ok, Is.True);
            await room.DrainPresenceForTesting(); var old = Socket;
            old.Fail(new Exception("sk_hidden")); await Rejected(admission);
            await Until(() => _sockets.Count == 2 && Uplink.State == PlayServUplinkState.Connected);
            Assert.That(room.RosterSnapshot(), Is.Empty);
            Assert.That(Socket.Sent.All(s => !s.Contains("rsv_test")), Is.True);
            Assert.That(room.Admission.TryAdmit("rsv_unused").Ok, Is.True);
        });
        [UnityTest] public IEnumerator DelayedRejectionCannotReachTheNextConfiguration() => H.Run(async () =>
        {
            var previousRoom = await Start(); await OfferAccepted();
            var previousAdmission = previousRoom.Admission.TryAdmit("rsv_test");
            Assert.That(previousAdmission.Ok, Is.True);
            await previousRoom.DrainPresenceForTesting();
            var previousRejections = _rejected;
            var delayed = new ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = action => delayed.Enqueue(action);
            Ack(ok: false);
            await ReceiveBarrier();
            Assert.That(delayed, Is.Not.Empty);
            Assert.That(previousRejections, Is.Empty);
            await Uplink.DisconnectAsync();

            Configure();
            var currentRoom = await Start(); await OfferAccepted();
            var currentAdmission = currentRoom.Admission.TryAdmit("rsv_test");
            Assert.That(currentAdmission.Ok, Is.True);
            await currentRoom.DrainPresenceForTesting();
            while (delayed.TryDequeue(out var callback)) callback();

            Assert.That(_rejected, Is.Empty, "The old callback must not enter the current configuration's queue.");
            Assert.That(previousRejections.Single().AdmissionId, Is.EqualTo(previousAdmission.AdmissionId));
            Assert.That(currentRoom.RosterSnapshot(), Is.EqualTo(new[] { Player }));
        });
        [UnityTest] public IEnumerator ReleaseCloseAndConfiguredBoundsDoNotLeakEntries() => H.Run(async () =>
        {
            Configure(o => o.AdmissionEntryLimit = 1); var room = await Start(); await OfferAccepted();
            Socket.Push(Offer("rsv_full")); await Until(() => Socket.Sent.Any(s => s.Contains("rsv_full") && s.Contains("room_refused")));
            await room.Admission.ReleaseAsync("rsv_test");
            Assert.That(Socket.Sent.Last(), Does.Contain("ticket_release")); Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.False);
            await room.CloseAsync(); Assert.That(Uplink.RetainedAdmissionCount, Is.Zero);
            Configure(o => o.AdmissionByteLimit = 1); await Start(); Socket.Push(Offer());
            await Until(() => Socket.Sent.Any(s => s.Contains("room_refused"))); Assert.That(Uplink.RetainedAdmissionCount, Is.Zero);
        });
        [UnityTest] public IEnumerator RegistrationAnswersOffersBeforeHttpResponseAndCleansFailure() => H.Run(async () =>
        {
            _http.Handler = async (request, ct) =>
            {
                if (request.RelativePath.EndsWith("upsert"))
                {
                    Socket.Push(Offer()); await Until(() => Socket.Sent.Any(s => s.Contains("ticket_result") && s.Contains("true")));
                    return new PlayServGameServerHttpResponse { StatusCode = 500, Body = "{}" };
                }
                return new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{}" };
            };
            await H.Error(() => Start()); Assert.That(Uplink.RetainedAdmissionCount, Is.Zero);
        });
        [UnityTest] public IEnumerator ZeroTtlAndUnknownRoomAreTypedRefusals() => H.Run(async () =>
        {
            var room = await Start(); Socket.Push(Offer(ttl: "0")); Socket.Push(Offer("rsv_unknown", "missing"));
            await Until(() => Socket.Sent.Count(s => s.Contains("ticket_result")) == 2);
            Assert.That(Socket.Sent.Any(s => s.Contains("reservation_expired")), Is.True);
            Assert.That(Socket.Sent.Any(s => s.Contains("room_closed")), Is.True);
            Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.False); Assert.That(Uplink.RetainedAdmissionCount, Is.Zero);
        });
        [UnityTest] public IEnumerator HookFailureIsSafeAndDuplicateCannotRestartPolicy() => H.Run(async () =>
        {
            var calls = 0;
            Configure(o => o.TicketOfferHandler = (_, ct) => { calls++; throw new Exception("sk_secret rsv_test a.b.c"); });
            var room = await Start(); Socket.Push(Offer()); await Until(() => Uplink.LastError.SourceCode == "ticket_offer_failed");
            Assert.That(Uplink.LastError.Message, Does.Not.Contain("sk_secret").And.Not.Contain("a.b.c"));
            Socket.Push(Offer()); await Until(() => Socket.Sent.Any(s => s.Contains("reservation_consumed")));
            Assert.That(calls, Is.EqualTo(1)); Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.False);
        });
        [UnityTest] public IEnumerator SuccessfulAckSurvivesAckDeadlineAndDisconnect() => H.Run(async () =>
        {
            Configure(o => o.HttpTimeout = TimeSpan.FromMilliseconds(500));
            var room = await Start(); await OfferAccepted();
            Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.True);
            await room.DrainPresenceForTesting(); Ack(); await ReceiveBarrier();
            _now += 1; await Uplink.SweepAdmissionsAsync();
            Assert.That(_rejected, Is.Empty); Assert.That(room.RosterSnapshot(), Is.EqualTo(new[] { Player }));
            Socket.Fail(new Exception()); await Until(() => _sockets.Count == 2 && Uplink.State == PlayServUplinkState.Connected);
            Assert.That(_rejected, Is.Empty); Assert.That(room.RosterSnapshot(), Is.EqualTo(new[] { Player }));
        });
        [UnityTest] public IEnumerator ReleasePreCancellationAndInvalidArgumentsNeverSpendTicket() => H.Run(async () =>
        {
            var room = await Start(); await OfferAccepted(); var count = Socket.Sent.Count;
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await room.Admission.ReleaseAsync("rsv_test", ct: canceled.Token); Assert.Fail("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Assert.Throws<ArgumentException>(() => room.Admission.TryAdmit("sk_not_a_ticket"));
            Assert.Throws<ArgumentException>(() => room.Admission.ReleaseAsync("rsv_test", "not_a_wire_reason"));
            Assert.That(Socket.Sent.Count, Is.EqualTo(count)); Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.True);
        });
        [UnityTest] public IEnumerator OtherRoomCannotTakeTicketAndShutdownClearsState() => H.Run(async () =>
        {
            var first = await Start(); var other = await Start("two"); await OfferAccepted();
            Assert.That(other.Admission.TryAdmit("rsv_test").Error.SourceCode, Is.EqualTo("room_mismatch"));
            Assert.That(first.Admission.TryAdmit("rsv_test").Ok, Is.True);
            await PlayServGameServer.ShutdownAsync();
            Assert.That(Uplink.RetainedAdmissionCount, Is.Zero); Assert.That(Uplink.AdmissionMode, Is.EqualTo(PlayServAdmissionMode.Unknown));
        });
        [UnityTest] public IEnumerator JoinThenLeaveKeepsStreamSequenceAndSendOrder() => H.Run(async () =>
        {
            var room = await Start(); await OfferAccepted(); room.Admission.TryAdmit("rsv_test"); room.ReportLeave(Player);
            await room.DrainPresenceForTesting();
            var frames = Socket.Sent.Where(s => s.Contains("room_presence") && !s.Contains("roster")).ToArray();
            Assert.That(frames.Length, Is.EqualTo(2)); Assert.That(frames[0], Does.Contain("\"join\"")); Assert.That(frames[1], Does.Contain("\"leave\""));
        });
    }
}
