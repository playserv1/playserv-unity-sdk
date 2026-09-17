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
    public sealed class PlayServRoomsNamespaceTests
    {
        private H.Http _http;
        private H.Key _key;
        private double _seconds;
        private DateTimeOffset _wall;
        private const string Reservation = "{\"status\":\"matched\",\"room_name\":\"room-one\",\"reservation_token\":\"rsv_secret\",\"expires_at\":\"2026-09-12T00:00:30Z\"";

        [SetUp] public void Setup() => Configure(false);

        [Test] public void ConfigurationReadIsStrictAndDoesNotOpenOrChangeUplink()
        {
            Respond(H.Config);
            var result = PlayServGameServer.GetRoomConfigurationAsync("arena").GetAwaiter().GetResult();
            Assert.That(result.Capacity, Is.EqualTo(8)); Assert.That(result.Version, Is.EqualTo(1));
            Assert.That(_http.Requests.Single().RelativePath, Is.EqualTo("rooms/arena/config"));
            Assert.That(PlayServGameServer.Uplink.RoomConfiguration, Is.Null);
            foreach (var body in new[] { "{}", H.Config.Replace("\"capacity\":8", "\"capacity\":\"8\""), H.Config.Replace("\"version\":1", "\"version\":1.5") })
            {
                Respond(body); Assert.Throws<PlayServGameServerException>(() => PlayServGameServer.GetRoomConfigurationAsync("arena").GetAwaiter().GetResult());
            }
        }

        [Test] public void ServerNamedJoinUsesExplicitPlayerAndRequiresMatchedTtl()
        {
            Respond(Reservation + ",\"expires_in\":10}");
            var result = PlayServGameServer.JoinRoomForPlayerAsync("arena", "room:one", "plr_one", new { team = "blue" }).GetAwaiter().GetResult();
            Assert.That(result.IsMatched, Is.True);
            var request = _http.Requests.Single();
            Assert.That(request.RelativePath, Is.EqualTo("rooms/arena/room%3Aone:join"));
            Assert.That(request.JsonBody, Is.EqualTo("{\"player_id\":\"plr_one\",\"params\":{\"team\":\"blue\"}}"));
            Assert.That(request.ServerKey, Is.Not.Empty);
            foreach (var body in new[] { Reservation + "}", "{\"status\":\"searching\"}", Reservation + ",\"expires_in\":\"10\"}" })
            {
                Respond(body);
                Assert.Throws<PlayServGameServerException>(() => PlayServGameServer.JoinRoomForPlayerAsync("arena", "room", "plr_one").GetAwaiter().GetResult());
            }
        }
        private void Configure(bool uplink)
        {
            PlayServGameServer.CancelForModuleShutdown();
            _http = new H.Http(); _key = new H.Key(); _seconds = 100;
            _wall = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", InstanceId = "instance-one",
                ServerKeyProvider = _key, EnableUplink = uplink
            }, _http, () => _wall, (_, ct) => Task.Delay(Timeout.Infinite, ct));
            PlayServGameServer.GetContextForServices().MonotonicSeconds = () => _seconds;
            _http.Handler = (r, ct) => Task.FromResult(Response(
                r.RelativePath.EndsWith("servers:launch") ? "{\"deployment_id\":\"dep_one\",\"region\":\"eu\"}" :
                r.RelativePath.EndsWith(":upsert") ? "{\"created\":true,\"placement\":{\"state\":\"active\",\"open\":true},\"room_config\":" + H.Config + ",\"roster_check\":\"ok\"}" :
                r.Method == "GET" ? "[]" : "{}"));
            PlayServGameServer.Uplink.SocketFactory = () => new H.Socket(H.Ack());
        }
        [TearDown] public void Teardown() => PlayServGameServer.CancelForModuleShutdown();
        private static PlayServGameServerHttpResponse Response(string body, int status = 200) =>
            new PlayServGameServerHttpResponse { Body = body, StatusCode = status };
        private void Respond(string body, int status = 200) => _http.Handler = (r, ct) => Task.FromResult(Response(body, status));
        private static Task<PlayServServerMatchResult> Find(CancellationToken ct = default) =>
            PlayServGameServer.FindMatchForPlayerAsync(new PlayServServerFindMatchRequest
            { FunctionSlug = "arena", PlayerId = "plr_one", Matchmaker = "ranked", Parameters = new { skill = 7 }, WaitMs = 20 }, ct);

        [UnityTest] public IEnumerator RestOnlyRoutesPreserveBodiesAndRotatingCredentials() => H.Run(async () =>
        {
            await PlayServGameServer.ListRoomsAsync("arena");
            _key.Value = "sk_rotated";
            await PlayServGameServer.UpsertRoomAsync("arena", new PlayServGameRoomSnapshot("room:one", 2, 8,
                attributes: new { map = "forest" }, connect: new PlayServGameRoomConnect("host", 7777, "udp"), region: "eu"));
            await PlayServGameServer.LaunchServerAsync("arena", "eu");
            await PlayServGameServer.CloseRoomAsync("arena", "room:one");
            var requests = _http.Requests.ToArray();
            Assert.That(requests.Select(r => r.RelativePath), Is.EqualTo(new[]
            { "rooms/arena", "rooms/arena:upsert", "rooms/arena/servers:launch", "rooms/arena/room%3Aone:close" }));
            Assert.That(requests.Select(r => r.Method), Is.EqualTo(new[] { "GET", "POST", "POST", "POST" }));
            Assert.That(requests[0].ServerKey, Is.EqualTo("sk_uplink_test"));
            Assert.That(requests.Skip(1).All(r => r.ServerKey == "sk_rotated" && r.Headers == null), Is.True);
            Assert.That(requests[0].JsonBody, Is.Null); Assert.That(requests[3].JsonBody, Is.Null);
            Assert.That(requests[1].JsonBody, Does.Contain("\"room_name\":\"room:one\"").And.Contain("\"players\":2")
                .And.Contain("\"map\":\"forest\"").And.Contain("\"port\":7777"));
            Assert.That(requests[2].JsonBody, Is.EqualTo("{\"region\":\"eu\"}"));
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Disconnected));
        });

        [UnityTest] public IEnumerator ManagedRoomHeartbeatAndShutdownUseNewRoutesAndSession() => H.Run(async () =>
        {
            Configure(true);
            var room = await H.Start("room:one");
            await room.HeartbeatAsync();
            await PlayServGameServer.ListRoomsAsync("arena");
            await PlayServGameServer.LaunchServerAsync("arena");
            var result = await PlayServGameServer.ShutdownAsync();
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.Rooms.Count, Is.EqualTo(1));
            var requests = _http.Requests.ToArray();
            Assert.That(requests.Select(r => r.RelativePath), Is.EqualTo(new[]
            { "rooms/arena:upsert", "rooms/arena:upsert", "rooms/arena", "rooms/arena/servers:launch", "rooms/arena/room%3Aone:close" }));
            Assert.That(requests.All(r => r.ServerKey == "session-one" && r.Headers == null), Is.True);
            Assert.That(requests[0].JsonBody, Does.Contain("\"instance_id\":\"instance-one\"").And.Contain("\"roster_hash\""));
        });

        [Test] public void ReservationFreezesMetadataAndCountsFromResponseTimeNotWallClock()
        {
            _http.Handler = (r, ct) =>
            {
                _seconds = 150; // Time spent awaiting the response must not consume its new TTL.
                return Task.FromResult(Response(Reservation + ",\"expires_in\":30,\"connect\":{\"host\":\"host\",\"port\":7777,\"transport\":\"udp\",\"connect_string\":\"opaque-endpoint\"},\"region\":\"eu\",\"attributes\":{\"map\":\"forest\",\"nested\":{\"round\":1}}}"));
            };
            var result = Find().GetAwaiter().GetResult();
            var reservation = result.Reservation;
            Assert.That(result.IsMatched, Is.True); Assert.That(reservation.ExpiresIn, Is.EqualTo(30));
            Assert.That(reservation.RemainingLifetime, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(reservation.Connect.Host, Is.EqualTo("host")); Assert.That(reservation.Connect.Port, Is.EqualTo(7777));
            Assert.That(reservation.Connect.Transport, Is.EqualTo("udp")); Assert.That(reservation.Connect.ConnectString, Is.EqualTo("opaque-endpoint"));
            Assert.That(reservation.Region, Is.EqualTo("eu")); Assert.That(reservation.Token, Is.EqualTo("rsv_secret"));
            var attributes = (IDictionary<string, object>)reservation.Attributes;
            attributes["map"] = "changed"; ((IDictionary<string, object>)attributes["nested"])["round"] = 99;
            Assert.That(PlayServGameServerJson.Serialize(reservation.Attributes), Is.EqualTo("{\"map\":\"forest\",\"nested\":{\"round\":1}}"));
            _wall = _wall.AddYears(-40); _seconds = 151.5;
            Assert.That(reservation.RemainingLifetime, Is.EqualTo(TimeSpan.FromSeconds(28.5)));
            _wall = _wall.AddYears(80); _seconds = 500;
            Assert.That(reservation.RemainingLifetime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(reservation.ExpiresAt, Is.EqualTo(new DateTimeOffset(2026, 9, 12, 0, 0, 30, TimeSpan.Zero)));
            var request = _http.Requests.Single();
            Assert.That(request.RelativePath, Is.EqualTo("matchmaking/arena/find"));
            Assert.That(request.JsonBody, Is.EqualTo("{\"player_id\":\"plr_one\",\"matchmaker\":\"ranked\",\"params\":{\"skill\":7},\"wait_ms\":20,\"search_age_ms\":0}"));
        }

        [TestCase("")][TestCase(",\"expires_in\":null")][TestCase(",\"expires_in\":0")][TestCase(",\"expires_in\":2147483647")]
        public void MissingNullableAndBoundaryMetadataRemainsCompatible(string fields)
        {
            Respond(Reservation + fields + ",\"connect\":null,\"region\":null,\"attributes\":null}");
            var result = Find().GetAwaiter().GetResult().Reservation;
            Assert.That(result.Connect, Is.Null); Assert.That(result.Region, Is.Null); Assert.That(result.Attributes, Is.Null);
            if (fields.Contains(":0")) Assert.That(result.RemainingLifetime, Is.EqualTo(TimeSpan.Zero));
            else if (fields.Contains("2147483647")) Assert.That(result.RemainingLifetime, Is.EqualTo(TimeSpan.FromSeconds(int.MaxValue)));
            else { Assert.That(result.ExpiresIn, Is.Null); Assert.That(result.RemainingLifetime, Is.Null); }
        }

        [TestCase("-1")][TestCase("1.5")][TestCase("1.0")][TestCase("\"30\"")][TestCase("2147483648")]
        [TestCase("9223372036854775808")][TestCase("true")][TestCase("{}")][TestCase("[]")][TestCase("\"sk_secret\"")]
        public void InvalidTtlCannotCoerceAndNeverLeaksResponse(string value)
        {
            foreach (var name in new[] { "expires_in", "Expires_In" })
            {
                Respond(Reservation + ",\"" + name + "\":" + value + "}");
                var error = Assert.Throws<PlayServGameServerException>(() => Find().GetAwaiter().GetResult());
                Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
                Assert.That(error.ToString() + error.UnifiedError.RawDetails, Does.Not.Contain("rsv_secret").And.Not.Contain("sk_secret"));
            }
        }

        [TestCase("room_closed")][TestCase("reservation_expired")][TestCase("reservation_consumed")]
        [TestCase("reservation_invalid")][TestCase("room_mismatch")][TestCase("future_reason")]
        public void ConsumeCodesRemainVerdictsOnTheUnchangedRoute(string code)
        {
            Respond("{\"ok\":false,\"error\":\"refused rsv_secret\",\"error_code\":\"" + code + "\"}");
            var result = PlayServGameServer.ConsumeReservationAsync("arena", "rsv_secret", "plr_one", "room:one").GetAwaiter().GetResult();
            Assert.That(result.Ok, Is.False); Assert.That(result.ErrorCode, Is.EqualTo(code));
            Assert.That(result.Error, Does.Not.Contain("rsv_secret"));
            var request = _http.Requests.Single();
            Assert.That(request.RelativePath, Is.EqualTo("matchmaking/arena/reservations/rsv_secret:consume"));
            Assert.That(request.JsonBody, Is.EqualTo("{\"player_id\":\"plr_one\",\"room_name\":\"room:one\"}"));
        }

        [Test] public void RoomTypeNotFoundPreservesProblemDetailsWithoutRouteFallback()
        {
            Respond("{\"code\":\"room_type_not_found\",\"detail\":\"Unknown room type arena.\",\"status\":404}", 404);
            Func<Task>[] calls = {
                () => PlayServGameServer.ListRoomsAsync("arena"),
                () => PlayServGameServer.LaunchServerAsync("arena"),
                () => PlayServGameServer.UpsertRoomAsync("arena", new PlayServGameRoomSnapshot("one", 0, 8)),
                () => PlayServGameServer.CloseRoomAsync("arena", "one"), () => Find()
            };
            foreach (var call in calls)
            {
                var error = Assert.Throws<PlayServGameServerException>(() => call().GetAwaiter().GetResult());
                Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("room_type_not_found"));
                Assert.That(error.UnifiedError.HttpStatus, Is.EqualTo(404));
                Assert.That(error.UnifiedError.RawDetails, Does.Contain("Unknown room type arena."));
            }
            Assert.That(_http.Requests.Count, Is.EqualTo(calls.Length));
        }

        [UnityTest] public IEnumerator CancellationNeverRetriesOrFallsBack() => H.Run(async () =>
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var error = Assert.Throws<OperationCanceledException>(() => Find(canceled.Token).GetAwaiter().GetResult());
            Assert.That(_http.Requests, Is.Empty);
            using var active = new CancellationTokenSource();
            _http.Handler = async (r, ct) => { await Task.Delay(Timeout.Infinite, ct); return Response("{}"); };
            var pending = Find(active.Token); active.Cancel();
            try { await pending; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert.That(_http.Requests.Count, Is.EqualTo(1));
        });
    }
}
