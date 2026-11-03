using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServOperatorRoomActionsTests
    {
        private PlayServGameServerUplink _uplink;
        private PlayServGameServerUplinkTests.Socket _socket;
        private PlayServGameServerUplinkTests.Http _http;

        private async Task Configure(PlayServGameServerOptions options = null, string ack = null, Func<DateTimeOffset> utcNow = null)
        {
            PlayServGameServer.CancelForModuleShutdown();
            options = options ?? new PlayServGameServerOptions();
            options.BackendServerAddress = "https://api.playserv.test"; options.ExecutorSlug = "arena";
            options.ServerKeyProvider = new PlayServGameServerUplinkTests.Key();
            _http = new PlayServGameServerUplinkTests.Http();
            PlayServGameServer.ConfigureForTesting(options, _http, utcNow ?? (() => DateTimeOffset.UtcNow),
                (_, ct) => Task.Delay(Timeout.Infinite, ct));
            _uplink = PlayServGameServer.Uplink;
            _socket = new PlayServGameServerUplinkTests.Socket(ack ?? PlayServGameServerUplinkTests.Ack());
            _uplink.SocketFactory = () => _socket;
            await _uplink.ConnectAsync();
        }

        private async Task Barrier()
        {
            var count = _socket.Sent.Count(s => s == "{\"type\":\"pong\"}");
            _socket.Push("{\"type\":\"ping\"}");
            await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Count(s => s == "{\"type\":\"pong\"}") > count);
        }
        private void Close(string id = "close-one") => _socket.Push("{\"type\":\"room_close\",\"request_id\":\"" + id + "\",\"room_name\":\"one\"}");
        private Task<PlayServGameRoomHandle> Start() => PlayServGameServer.StartRoomAsync(
            new PlayServStartRoomRequest("arena", new PlayServGameRoomSnapshot("one", 0, 8)));

        [UnityTest] public IEnumerator CancellationAfterRegistrationCommitStillReturnsTheOwnedHandle() => PlayServGameServerUplinkTests.Run(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var cancelAtCommit = false;
            await Configure(utcNow: () =>
            {
                if (cancelAtCommit) cancellation.Cancel();
                return DateTimeOffset.UtcNow;
            });
            cancelAtCommit = true;
            var room = await PlayServGameServer.StartRoomAsync(
                new PlayServStartRoomRequest("arena", new PlayServGameRoomSnapshot("one", 0, 8)), cancellation.Token);
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Active));
            await room.CloseAsync();
        });

        [UnityTest] public IEnumerator CreationOutcome_IdentifiesExactPreparationAfterSameNameReplacement() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var property = typeof(PlayServRoomCreationOutcome).GetProperty("Request");
            Assert.That(property, Is.Not.Null, "Cleanup needs the exact preparation identity, not just a reused room name.");
            PlayServRoomCreateContext preparation = null;
            var outcomes = new System.Collections.Concurrent.ConcurrentQueue<PlayServRoomCreationOutcome>();
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => Task.FromResult(true), RoomFactory = (request, ct) =>
            {
                preparation = request;
                return Task.FromResult(PlayServRoomCreateDecision.Accept(new PlayServGameRoomSnapshot(request.RoomName, 0, 8,
                    connect: new PlayServGameRoomConnect("game.test", 7777, "udp"))));
            } });
            _uplink.RoomCreationCompleted += outcome => outcomes.Enqueue(outcome);
            _socket.Push("{\"type\":\"room_create\",\"room_name\":\"one\"}");
            await PlayServGameServerUplinkTests.Until(() => outcomes.Count == 1);
            var first = preparation;
            Close(); await Barrier(); await PlayServGameServerUplinkTests.Until(() => _uplink.PendingOperatorActions == 0);
            _socket.Push("{\"type\":\"room_create\",\"room_name\":\"one\"}");
            await PlayServGameServerUplinkTests.Until(() => outcomes.Count == 2);
            Assert.That(property.GetValue(outcomes.First()), Is.SameAs(first));
            Assert.That(property.GetValue(outcomes.First()), Is.Not.SameAs(preparation), "An old queued cleanup cannot identify the replacement by room name.");
            Assert.That(property.GetValue(outcomes.Last()), Is.SameAs(preparation));
        });

        [UnityTest] public IEnumerator AbsentTarget_RemainsReservedUntilGameHandlerReallyCompletes() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var done = new TaskCompletionSource<bool>(); var called = false;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => { called = true; return done.Task; } });
            Close(); await Barrier(); await PlayServGameServerUplinkTests.Until(() => called);
            try { await Start(); Assert.Fail("An active operator handler must reserve the room name."); }
            catch (InvalidOperationException) { }
            done.SetResult(true); await PlayServGameServerUplinkTests.Until(() => _uplink.PendingOperatorActions == 0);
            Assert.That((await Start()).State, Is.EqualTo(PlayServGameRoomState.Active));
        });

        [UnityTest] public IEnumerator ManualClose_DoesNotReleaseNameUnderPendingOperatorRemoval() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var done = new TaskCompletionSource<bool>(); var called = false;
            await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => { called = true; return done.Task; } });
            var room = await Start();
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"r\",\"room_name\":\"one\",\"player_id\":\"plr_one\"}");
            await Barrier(); await PlayServGameServerUplinkTests.Until(() => called);
            await room.CloseAsync();
            try { await Start(); Assert.Fail("Manual close must keep an active operator handler's room name reserved."); }
            catch (InvalidOperationException) { }
            finally { done.TrySetResult(true); }
            await PlayServGameServerUplinkTests.Until(() => _uplink.PendingOperatorActions == 0);
            Assert.That((await Start()).State, Is.EqualTo(PlayServGameRoomState.Active));
        });

        [UnityTest] public IEnumerator OperatorRemoval_DropsQueuedTicketJoinAndOldRoster() => PlayServGameServerUplinkTests.Run(async () =>
        {
            await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => Task.FromResult(true) });
            var room = await Start(); var blocked = new TaskCompletionSource<bool>();
            await PlayServGameServerUplinkTests.OfferAccepted(_socket.Push, _socket.Sent, room, "plr_one", "rsv_queued");
            room.QueueAdmissionJoin(() => blocked.Task);
            Assert.That(room.Admission.TryAdmit("rsv_queued").Ok, Is.True); room.RepairRoster();
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"r\",\"room_name\":\"one\",\"player_id\":\"plr_one\"}");
            await Barrier(); blocked.SetResult(true); await room.DrainPresenceForTesting();
            Assert.That(_socket.Sent.Count(s => s.Contains("\"event\":\"join\"")), Is.Zero);
            Assert.That(_socket.Sent.Count(s => s.Contains("\"event\":\"roster\"") && s.Contains("plr_one")), Is.Zero);
            Assert.That(_socket.Sent.Count(s => s.Contains("\"event\":\"leave\"")), Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator OperatorRemoval_FullPresenceQueueStillReportsOneLeave() => PlayServGameServerUplinkTests.Run(async () =>
        {
            await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => Task.FromResult(true) });
            var room = await Start();
            await PlayServGameServerUplinkTests.AdmitAndAcknowledge(_socket.Push, _socket.Sent, room, "plr_one");
            var blocked = new TaskCompletionSource<bool>();
            for (var i = 0; i < 256; i++) room.QueueAdmissionJoin(() => blocked.Task);
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"r\",\"room_name\":\"one\",\"player_id\":\"plr_one\"}");
            await Barrier();
            Assert.That(_socket.Sent.Any(s => s.Contains("participant_remove_result")), Is.False);
            blocked.SetResult(true); await room.DrainPresenceForTesting();
            Assert.That(_socket.Sent.Count(s => s.Contains("\"event\":\"leave\"")), Is.EqualTo(1));
            await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Any(s => s.Contains("participant_remove_result")));
            Assert.That(_socket.Sent.Last(), Does.Contain("\"ok\":true"));
        });

        [UnityTest] public IEnumerator OperatorRemoval_ExhaustedReservedPresenceCapacityFailsSafely() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var called = false;
            await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => { called = true; return Task.FromResult(true); } });
            var room = await Start();
            await PlayServGameServerUplinkTests.AdmitAndAcknowledge(_socket.Push, _socket.Sent, room, "plr_one");
            var blocked = new TaskCompletionSource<bool>();
            for (var i = 0; i < 320; i++) room.QueueAdmissionJoin(() => blocked.Task);
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"r\",\"room_name\":\"one\",\"player_id\":\"plr_one\"}");
            try
            {
                await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Any(s => s.Contains("participant_remove_result")));
                Assert.That(called, Is.True); Assert.That(room.RosterSnapshot(), Is.Empty);
                Assert.That(_socket.Sent.Last(), Is.EqualTo("{\"type\":\"participant_remove_result\",\"request_id\":\"r\",\"ok\":false,\"reason\":\"operator_action_failed\"}"));
            }
            finally { blocked.TrySetResult(true); }
            await room.DrainPresenceForTesting();
        });

        [UnityTest] public IEnumerator OperatorRemoval_QueuedLeaveIsNotReplayedAfterReconnect() => PlayServGameServerUplinkTests.Run(async () =>
        {
            await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => Task.FromResult(true) });
            var room = await Start();
            await PlayServGameServerUplinkTests.AdmitAndAcknowledge(_socket.Push, _socket.Sent, room, "plr_one");
            var blocked = new TaskCompletionSource<bool>(); room.QueueAdmissionJoin(() => blocked.Task);
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"r\",\"room_name\":\"one\",\"player_id\":\"plr_one\"}");
            await Barrier(); await _uplink.DisconnectAsync();
            _socket = new PlayServGameServerUplinkTests.Socket(PlayServGameServerUplinkTests.Ack());
            _uplink.SocketFactory = () => _socket;
            await _uplink.ConnectAsync(); blocked.SetResult(true); await room.DrainPresenceForTesting();
            Assert.That(_socket.Sent.Any(s => s.Contains("\"event\":\"leave\"") || s.Contains("participant_remove_result")), Is.False);
        });

        private void Offer(string token) => _socket.Push("{\"type\":\"ticket_offer\",\"reservation_token\":\"" + token + "\",\"room_name\":\"one\",\"player_id\":\"plr_one\",\"expires_in\":30}");
        [UnityTest] public IEnumerator OperatorRemoval_RevokesActiveAndParkedAdmissionWithoutDoubleDisconnect() => PlayServGameServerUplinkTests.Run(async () =>
        {
            foreach (var parked in new[] { false, true })
            {
                if (_uplink != null) await _uplink.DisconnectAsync();
                await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => Task.FromResult(true) });
                var room = await Start(); var tracker = room.CreateConnectionTracker();
                var disconnects = 0; var rejections = 0;
                tracker.ConnectionRemoved += _ => disconnects++;
                room.Admission.AdmissionRejected += _ => rejections++;
                Offer("rsv_first"); await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Any(s => s.Contains("ticket_result")));
                Assert.That(tracker.TryAdmit("connection-one", "rsv_first").Ok, Is.True);
                await room.DrainPresenceForTesting();
                if (parked) tracker.ReportDisconnected("connection-one");
                Offer("rsv_unused"); await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Count(s => s.Contains("ticket_result")) == 2);
                _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"r\",\"room_name\":\"one\",\"player_id\":\"plr_one\"}");
                await Barrier(); await room.DrainPresenceForTesting();
                Assert.That(room.RosterSnapshot(), Is.Empty);
                Assert.That(tracker.TryGetPlayerId("connection-one", out _), Is.False);
                Assert.That(tracker.TryAdmit("connection-two", "rsv_unused").Ok, Is.False);
                _socket.Push("{\"type\":\"join_ack\",\"reservation_token\":\"rsv_first\",\"room_name\":\"one\",\"player_id\":\"plr_one\",\"ok\":false,\"reason\":\"room_refused\"}");
                Offer("rsv_late"); await Barrier();
                await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Count(s => s.Contains("ticket_result")) == 3);
                Assert.That(_socket.Sent.Last(s => s.Contains("ticket_result")), Does.Contain("\"ok\":false").And.Contain("room_refused"));
                Assert.That(disconnects, Is.Zero); Assert.That(rejections, Is.Zero);
                Assert.That(_socket.Sent.Count(s => s.Contains("\"event\":\"leave\"")), Is.EqualTo(1));
                await room.CloseAsync();
            }
        });

        [UnityTest] public IEnumerator OperatorClose_LateHeartbeatCannotReactivateRoom() => PlayServGameServerUplinkTests.Run(async () =>
        {
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => Task.FromResult(true) });
            var room = await Start(); var response = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, ct) => response.Task;
            var heartbeat = room.HeartbeatAsync(); await PlayServGameServerUplinkTests.Until(() => _http.Requests.Count == 2);
            Close(); await Barrier();
            response.SetResult(new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{\"created\":true,\"placement\":{\"state\":\"active\",\"open\":true}}" });
            try { await heartbeat; Assert.Fail("Heartbeat must observe operator closure."); } catch (OperationCanceledException) { }
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Closed));
            Assert.That(_http.Requests.Count, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator OperatorClose_StopsRoomWithoutSendingAnotherRestClose() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var completion = new TaskCompletionSource<bool>();
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => completion.Task });
            var room = await Start();
            Close(); await Barrier();
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Closed));
            Assert.That(_http.Requests.Any(r => r.RelativePath.EndsWith(":close")), Is.False);
            completion.SetResult(true);
            await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Any(s => s.Contains("room_close_result")));
            Assert.That((await Start()).State, Is.EqualTo(PlayServGameRoomState.Active), "A deliberate new generation remains possible.");
        });

        [UnityTest] public IEnumerator OperatorClose_LateHeartbeatCannotChangeReplacementUplinkConfiguration() => PlayServGameServerUplinkTests.Run(async () =>
        {
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => Task.FromResult(true) });
            var room = await Start();
            var response = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, ct) => response.Task;
            var heartbeat = room.HeartbeatAsync();
            await PlayServGameServerUplinkTests.Until(() => _http.Requests.Count == 2);
            Close(); await Barrier();
            await PlayServGameServerUplinkTests.Until(() => _uplink.PendingOperatorActions == 0);
            await _uplink.DisconnectAsync();
            await Configure();
            var version = _uplink.RoomConfiguration.Version;
            response.SetResult(new PlayServGameServerHttpResponse { StatusCode = 200,
                Body = "{\"created\":false,\"placement\":{\"state\":\"active\",\"open\":true},\"room_config\":" +
                    PlayServGameServerUplinkTests.Config.Replace("\"version\":1", "\"version\":99") + "}" });
            try { await heartbeat; Assert.Fail("The closed generation must remain canceled."); }
            catch (OperationCanceledException) { }
            Assert.That(_uplink.RoomConfiguration.Version, Is.EqualTo(version),
                "An old upsert must not apply its configuration to the replacement uplink.");
        });

        [UnityTest] public IEnumerator OperatorClose_DuringRegistrationCannotActivateItsLateResponse() => PlayServGameServerUplinkTests.Run(async () =>
        {
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => Task.FromResult(true) });
            var response = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, ct) => response.Task;
            var start = Start();
            await PlayServGameServerUplinkTests.Until(() => _http.Requests.Count == 1);
            Close(); await Barrier();
            response.SetResult(new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{\"created\":true,\"placement\":{\"state\":\"active\",\"open\":true}}" });
            try { await start; Assert.Fail("A closed registration must not publish a handle."); }
            catch (OperationCanceledException) { }
            Assert.That(_http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator OperatorClose_DuringFactoryCancelsCreationBeforeUpsert() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var factory = new TaskCompletionSource<PlayServRoomCreateDecision>();
            var invoked = false; CancellationToken token = default;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => Task.FromResult(true),
                RoomFactory = (_, ct) => { invoked = true; token = ct; return factory.Task; } });
            _socket.Push("{\"type\":\"room_create\",\"room_name\":\"one\"}");
            await PlayServGameServerUplinkTests.Until(() => invoked);
            Close(); await Barrier();
            Assert.That(token.IsCancellationRequested, Is.True);
            factory.SetResult(PlayServRoomCreateDecision.Accept(new PlayServGameRoomSnapshot("one", 0, 8,
                connect: new PlayServGameRoomConnect("game.test", 7777, "udp"))));
            await _uplink.DrainRoomCreationAsync();
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(_socket.Sent.Any(s => s.Contains("room_create_result")), Is.False);
        });

        [UnityTest] public IEnumerator OperatorRemove_RevokesAdmittedRosterBeforeHandlerCompletes() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var completion = new TaskCompletionSource<bool>();
            await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => completion.Task });
            var room = await Start();
            await PlayServGameServerUplinkTests.AdmitAndAcknowledge(_socket.Push, _socket.Sent, room, "plr_one");
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"r\",\"room_name\":\"one\",\"player_id\":\"plr_one\"}");
            await Barrier(); await room.DrainPresenceForTesting();
            Assert.That(room.RosterSnapshot(), Is.Empty);
            Assert.That(_socket.Sent.Count(s => s.Contains("\"event\":\"leave\"")), Is.EqualTo(1));
            Assert.Throws<PlayServGameServerException>(() => room.ReportJoin("plr_one"));
            completion.SetResult(true);
        });

        [UnityTearDown] public IEnumerator Cleanup() => PlayServGameServerUplinkTests.Run(async () =>
        {
            try { if (_uplink != null) await _uplink.DisconnectAsync(); }
            finally { PlayServGameServer.CancelForModuleShutdown(); }
        });

        [UnityTest] public IEnumerator OperatorRemoval_AdvertisesAndAcknowledgesOnlyAfterGameCleanup() => PlayServGameServerUplinkTests.Run(async () =>
        {
            var completion = new TaskCompletionSource<bool>();
            Func<object, CancellationToken, Task<bool>> handler = (_, ct) => completion.Task;
            var options = new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena",
                ServerKeyProvider = new PlayServGameServerUplinkTests.Key()
            };
            var property = typeof(PlayServGameServerOptions).GetProperty("ParticipantRemoveHandler");
            Assert.That(property, Is.Not.Null, "A game-owned completion handler is required before advertising participant_remove.");
            property.SetValue(options, handler);
            PlayServGameServer.ConfigureForTesting(options, new PlayServGameServerUplinkTests.Http(),
                () => DateTimeOffset.UtcNow, (_, ct) => Task.Delay(Timeout.Infinite, ct));
            _uplink = PlayServGameServer.Uplink;
            _socket = new PlayServGameServerUplinkTests.Socket(PlayServGameServerUplinkTests.Ack());
            _uplink.SocketFactory = () => _socket;
            await _uplink.ConnectAsync();
            Assert.That(_socket.Sent.First(), Does.Contain("participant_remove"));
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"remove-one\",\"room_name\":\"absent\",\"player_id\":\"plr_one\"}");
            _socket.Push("{\"type\":\"ping\"}");
            await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Contains("{\"type\":\"pong\"}"));
            Assert.That(_socket.Sent.Any(s => s.Contains("participant_remove_result")), Is.False);
            completion.SetResult(true);
            await PlayServGameServerUplinkTests.Until(() => _socket.Sent.Any(s => s.Contains("participant_remove_result")));
            Assert.That(_socket.Sent.Last(), Is.EqualTo("{\"type\":\"participant_remove_result\",\"request_id\":\"remove-one\",\"ok\":true}"));
        });
    }
}
