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
    // Protocol fixtures, not proof of deployed PSV-2590 room-create dispatch.
    public sealed class PlayServAdmissionInteroperabilityTests
    {
        private H.Http _http;
        private ConcurrentQueue<H.Socket> _sockets;
        private ConcurrentQueue<PlayServRoomCreationOutcome> _created;
        private H.AdmissionTestScope _scope;
        private double _now;
        private DateTimeOffset _wall;
        private H.Socket Socket => _sockets.Last();
        private PlayServGameServerUplink Uplink => PlayServGameServer.Uplink;
        [SetUp] public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown();
            _scope = new H.AdmissionTestScope();
            _now = 100; _wall = DateTimeOffset.FromUnixTimeSeconds(10000);
            _http = new H.Http(); _sockets = new ConcurrentQueue<H.Socket>();
            _created = new ConcurrentQueue<PlayServRoomCreationOutcome>();
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(),
                RoomFactory = (request, ct) => Task.FromResult(PlayServRoomCreateDecision.Accept(new PlayServGameRoomSnapshot(
                    request.RoomName, 0, request.Configuration.Capacity, connect: new PlayServGameRoomConnect("game.test", 7777, "udp"))))
            }, _http, () => _wall, (_, ct) => Task.Delay(Timeout.Infinite, ct));
            PlayServGameServer.GetContextForServices().MonotonicSeconds = () => _now;
            Uplink.Seconds = () => _now;
            var uplink = Uplink; var sockets = _sockets; var created = _created;
            _scope.Track(uplink);
            Uplink.SocketFactory = () =>
            {
                var socket = new H.Socket(H.Ack(ttl: 4).Replace("\"consume\"", "\"push\"")); sockets.Enqueue(socket); return socket;
            };
            Action<PlayServRoomCreationOutcome> completed = result => created.Enqueue(result);
            uplink.RoomCreationCompleted += completed;
            _scope.UnsubscribeOnCleanup(() => uplink.RoomCreationCompleted -= completed);
        }
        [UnityTearDown] public IEnumerator Cleanup() => H.Run(() => _scope.CleanupAsync());
        private string Diagnostics() => $"Uplink={Uplink.State}; sockets={_sockets.Count}; created={_created.Count}.";
        private Task Until(Func<bool> predicate) => H.Until(predicate, Diagnostics);
        private Task Until(Func<Task<bool>> predicate) => H.Until(predicate, Diagnostics);
        private async Task<PlayServGameRoomHandle> Start()
        {
            var room = await PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest("arena", new PlayServGameRoomSnapshot("one", 0, 8)));
            _scope.Track(room);
            return room;
        }
        private void Renew(string token = "renewed-session", int ttl = 4) => Socket.Push(
            "{\"type\":\"session_token\",\"session_token\":\"" + token + "\",\"expires_in\":" + ttl + "}");
        private async Task WaitRenewed(string token = "renewed-session") => await Until(async () =>
            await Uplink.GetRestCredentialAsync(PlayServGameServer.GetContextForServices(), default) == token);
        private static PlayServGameServerHttpResponse Registered() => new PlayServGameServerHttpResponse
        {
            StatusCode = 200, Body = "{\"created\":true,\"placement\":{\"state\":\"active\",\"open\":true},\"room_config\":" + H.Config + ",\"roster_check\":\"ok\"}"
        };

        [UnityTest] public IEnumerator RenewedBearerSurvivesOriginalDeadlineAndWallClockChanges() => H.Run(async () =>
        {
            await Uplink.ConnectAsync(); var original = Socket;
            await PlayServGameServer.ListRoomsAsync("arena"); Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("session-one"));
            _now += 2; Renew(); await WaitRenewed();
            _now += 3; _wall = _wall.AddYears(10);
            await PlayServGameServer.ListRoomsAsync("arena"); Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("renewed-session"));
            Assert.That(_sockets.Count, Is.EqualTo(1)); Assert.That(Socket, Is.SameAs(original));
            _wall = _wall.AddYears(-20);
            await PlayServGameServer.ListRoomsAsync("arena"); Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("renewed-session"));
            _now += 2;
            await Until(() => _sockets.Count == 2 && Uplink.State == PlayServUplinkState.Connected);
            await PlayServGameServer.ListRoomsAsync("arena"); Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("session-one"));
            Assert.That(_http.Requests.Count, Is.EqualTo(4)); // no hidden mutation/read replay
        });
        [UnityTest] public IEnumerator RenewalDoesNotResetOfferedTicketAndExpiryStillUsesReceiptClock() => H.Run(async () =>
        {
            var room = await Start();
            Socket.Push(PlayServPushedAdmissionTests.Offer(ttl: "3"));
            await Until(() => Socket.Sent.Any(s => s.Contains("ticket_result")));
            _now += 1; Renew(ttl: 30); await WaitRenewed(); _wall = _wall.AddYears(-20);
            Assert.That(Uplink.RetainedAdmissionCount, Is.EqualTo(1));
            _now += 3; Assert.That(room.Admission.TryAdmit("rsv_test").Ok, Is.False);
            await Uplink.SweepAdmissionsAsync(); Assert.That(Uplink.RetainedAdmissionCount, Is.Zero);
            Assert.That(Socket.Sent.Any(s => s.Contains("ticket_release") && s.Contains("reservation_expired")), Is.True);
            Assert.That(_sockets.Count, Is.EqualTo(1));
        });
        [UnityTest] public IEnumerator RequestedRoomRegistersWhileAnsweringEarlyOfferWithRenewedSession() => H.Run(async () =>
        {
            var registrations = 0;
            _http.Handler = async (request, ct) =>
            {
                if (request.RelativePath.EndsWith("upsert"))
                {
                    registrations++;
                    Assert.That(request.RelativePath, Is.EqualTo("rooms/arena:upsert"));
                    Assert.That(request.ServerKey, Is.EqualTo("renewed-session"));
                    Assert.That(request.JsonBody, Does.Contain("\"room_name\":\"requested\"").And.Contain("\"capacity\":8"));
                    Assert.That(Socket.Sent.Last(), Is.EqualTo("{\"type\":\"room_create_result\",\"room_name\":\"requested\",\"ok\":true}"));
                    Socket.Push(PlayServPushedAdmissionTests.Offer(room: "requested"));
                    await Until(() => Socket.Sent.Any(s => s.Contains("ticket_result") && s.Contains("\"ok\":true")));
                    Assert.That(_created, Is.Empty); // upsert hasn't returned, but admission policy already answered
                    return Registered();
                }
                return new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{}" };
            };
            await Uplink.ConnectAsync(); Renew(ttl: 30); await WaitRenewed();
            Assert.That(Socket.Sent.First(), Does.Contain("\"capabilities\":[\"admission_push\",\"room_create\"]"));
            Socket.Push("{\"type\":\"room_create\",\"room_name\":\"requested\"}");
            await Until(() => _created.Count == 1);
            var outcome = _created.Single(); Assert.That(outcome.IsSuccess, Is.True, outcome.Error.SourceCode);
            _scope.Track(outcome.Room);
            Assert.That(outcome.Room.Configuration.Capacity, Is.EqualTo(8));
            var admitted = outcome.Room.Admission.TryAdmit("rsv_test"); Assert.That(admitted.Ok, Is.True);
            await outcome.Room.DrainPresenceForTesting();
            Assert.That(Socket.Sent.Count(s => s.Contains("\"event\":\"join\"")), Is.EqualTo(1));
            Assert.That(registrations, Is.EqualTo(1)); Assert.That(_http.Requests.Count, Is.EqualTo(1));
        });
        [UnityTest] public IEnumerator TerminatedHeartbeatClearsTicketsWithoutAnEventSubscriber() => H.Run(async () =>
        {
            var room = await Start();
            Socket.Push(PlayServPushedAdmissionTests.Offer()); await Until(() => Uplink.RetainedAdmissionCount == 1);
            _http.Handler = (_, ct) => Task.FromResult(new PlayServGameServerHttpResponse
            { StatusCode = 404, Body = "{\"code\":\"room_type_not_found\",\"detail\":\"Not a room type\"}" });
            await H.Error(() => room.HeartbeatAsync());
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Terminated)); Assert.That(Uplink.RetainedAdmissionCount, Is.Zero);
        });

        [UnityTest] public IEnumerator AmbiguousJoinSendRevokesExactConnectionWithoutReplay() => H.Run(async () =>
        {
            var originalFactory = Uplink.SocketFactory; var first = true;
            Uplink.SocketFactory = () =>
            {
                var socket = originalFactory();
                if (!first) return socket;
                first = false; return new AmbiguousJoinSocket(socket);
            };
            var room = await Start();
            var rejections = new ConcurrentQueue<PlayServRoomAdmissionResult>();
            Action<PlayServRoomAdmissionResult> rejected = result => rejections.Enqueue(result);
            room.Admission.AdmissionRejected += rejected;
            _scope.UnsubscribeOnCleanup(() => room.Admission.AdmissionRejected -= rejected);
            Socket.Push(PlayServPushedAdmissionTests.Offer());
            await Until(() => Socket.Sent.Any(s => s.Contains("ticket_result")));
            var accepted = room.Admission.TryAdmit("rsv_test"); Assert.That(accepted.Ok, Is.True);
            await H.Until(() => rejections.Any(result => result.AdmissionId == accepted.AdmissionId),
                () => Diagnostics() + $" Rejected={rejections.Count}.");
            Assert.That(rejections.Count, Is.EqualTo(1));
            Assert.That(rejections.Single().AdmissionId, Is.EqualTo(accepted.AdmissionId));
            Assert.That(rejections.Single().Error.SourceCode, Is.EqualTo("admission_send_outcome_unknown"));
            Assert.That(rejections.Single().Error.Message, Does.Not.Contain("sk_secret"));
            await Until(() => _sockets.Count == 2 && Uplink.State == PlayServUplinkState.Connected);
            Assert.That(room.RosterSnapshot(), Is.Empty);
            Assert.That(_sockets.Sum(s => s.Sent.Count(frame => frame.Contains("\"event\":\"join\""))), Is.EqualTo(1));
        });

        private sealed class AmbiguousJoinSocket : IPlayServUplinkSocket
        {
            private readonly IPlayServUplinkSocket _inner;
            internal AmbiguousJoinSocket(IPlayServUplinkSocket inner) { _inner = inner; }
            public Task ConnectAsync(Uri endpoint, string credential, CancellationToken ct) => _inner.ConnectAsync(endpoint, credential, ct);
            public Task<string> ReceiveAsync(CancellationToken ct) => _inner.ReceiveAsync(ct);
            public async Task SendAsync(byte[] bytes, CancellationToken ct)
            {
                await _inner.SendAsync(bytes, ct);
                if (Encoding.UTF8.GetString(bytes).Contains("\"event\":\"join\"")) throw new Exception("sk_secret ambiguous send");
            }
            public void Abort() => _inner.Abort();
            public void Dispose() => _inner.Dispose();
        }
    }
}
