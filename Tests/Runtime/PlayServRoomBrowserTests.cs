using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Matchmaking;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServRoomBrowserTests
    {
        private const string EmptyPage = "{\"data\":[],\"page\":{\"cursor_next\":null,\"has_more\":false}}";
        private const string Reservation = "{\"status\":\"matched\",\"room_name\":\"room:one\",\"reservation_token\":\"rsv_secret\",\"expires_at\":\"2000-01-01T00:00:00Z\",\"expires_in\":10,\"connect\":{\"host\":\"Game.Example.com\",\"port\":7777,\"transport\":\"udp\",\"connect_string\":\"opaque ? text\",\"region\":\"eu\"},\"region\":\"eu\",\"attributes\":{\"map\":\"arena\",\"nested\":{\"flags\":[true,2]}}}";

        [Test]
        public void Browse_snapshots_filters_before_token_resolution_and_pages_without_followup_io()
        {
            var http = new HttpFixture();
            http.Enqueue("{\"data\":[],\"page\":{\"cursor_next\":\"next+/=\",\"has_more\":true}}");
            http.Enqueue(EmptyPage);
            var attributes = new Dictionary<string, string> { ["map & mode"] = "arena + desert", ["round"] = "3" };
            var query = new PlayServRoomBrowseQuery { Limit = 2, PlacementState = "open", Region = "eu & west", Cursor = "old+/=", Attributes = attributes };
            var token = new TaskCompletionSource<string>();
            var client = Create(http, token: _ => token.Task);
            var pending = client.BrowseRoomsAsync("arena", query, default);
            Assert.That(http.Requests, Is.Empty);
            query.Limit = 200;
            query.Region = "changed";
            attributes["round"] = "999";
            token.SetResult("player.jwt.value");
            var first = pending.GetAwaiter().GetResult();
            var request = http.Requests[0];
            Assert.That(request.RelativePath, Is.EqualTo("rooms/arena:browse?limit=2&placement_state=open&region=eu%20%26%20west&cursor=old%2B%2F%3D&attributes.map%20%26%20mode=arena%20%2B%20desert&attributes.round=3"));
            AssertPlayerRequest(request, "GET");
            Assert.That(http.Requests, Has.Count.EqualTo(1));
            Assert.That(first.HasMore, Is.True);
            Assert.That(first.CursorNext, Is.EqualTo("next+/="));
            var last = client.BrowseRoomsAsync("arena", new PlayServRoomBrowseQuery { Cursor = first.CursorNext }, default).GetAwaiter().GetResult();
            Assert.That(http.Requests[1].RelativePath, Is.EqualTo("rooms/arena:browse?limit=50&cursor=next%2B%2F%3D"));
            Assert.That(last.Rooms, Is.Empty);
            Assert.That(last.HasMore, Is.False);
            Assert.That(last.CursorNext, Is.Null);
        }

        [Test]
        public void Browse_preserves_client_projection_including_unjoinable_and_reservation_only_rooms()
        {
            var http = new HttpFixture();
            http.Enqueue("{\"data\":[{\"room_name\":\"closing\",\"players\":8,\"capacity\":4,\"state\":\"combat\",\"placement_state\":\"session_closing\",\"region\":\"eu\",\"attributes\":{\"round\":3},\"connect\":{\"host\":\"localhost\",\"port\":7,\"transport\":\"custom\",\"connect_string\":\"verbatim?#\"},\"drain_until\":\"secret\",\"drain_cause\":\"secret\",\"instance_id\":\"secret\"},{\"room_name\":\"starting\",\"players\":0,\"capacity\":4,\"state\":null,\"placement_state\":\"open\",\"region\":null,\"attributes\":null,\"connect\":null}],\"page\":{\"has_more\":false}}");
            var page = Create(http).BrowseRoomsAsync("arena", null, default).GetAwaiter().GetResult();
            Assert.That(page.Rooms, Has.Count.EqualTo(2));
            Assert.That(page.Rooms[0].RoomName, Is.EqualTo("closing"));
            Assert.That(page.Rooms[0].Players, Is.EqualTo(8));
            Assert.That(page.Rooms[0].Capacity, Is.EqualTo(4));
            Assert.That(page.Rooms[0].State, Is.EqualTo("combat"));
            Assert.That(page.Rooms[0].PlacementState, Is.EqualTo("session_closing"));
            Assert.That(page.Rooms[0].Connect.Host, Is.EqualTo("localhost"));
            Assert.That(page.Rooms[0].Connect.Transport, Is.EqualTo("custom"));
            Assert.That(page.Rooms[0].Connect.ConnectString, Is.EqualTo("verbatim?#"));
            Assert.That(page.Rooms[1].Connect, Is.Null);
            Assert.That(page.Rooms[1].Region, Is.Null);
            Assert.That(page.Rooms[1].Attributes, Is.Null);
            Assert.That(typeof(PlayServRoomListing).GetProperty("DrainUntil"), Is.Null);
            Assert.That(typeof(PlayServRoomListing).GetProperty("DrainCause"), Is.Null);
            Assert.That(typeof(PlayServRoomListing).GetProperty("InstanceId"), Is.Null);
            Assert.Throws<NotSupportedException>(() => ((IList<PlayServRoomListing>)page.Rooms).Clear());
        }

        [TestCase(1)]
        [TestCase(200)]
        public void Browse_accepts_limit_boundary_and_four_filters(int limit)
        {
            var http = new HttpFixture(); http.Enqueue(EmptyPage);
            var query = new PlayServRoomBrowseQuery { Limit = limit, Region = new string('r', 32), Attributes = Filters(4) };
            Create(http).BrowseRoomsAsync("abc", query, default).GetAwaiter().GetResult();
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Browse_rejects_invalid_query_before_credentials_or_http()
        {
            var http = new HttpFixture();
            var resolutions = 0;
            var client = Create(http, token: _ => { resolutions++; return Task.FromResult("jwt"); });
            var queries = new[] {
                new PlayServRoomBrowseQuery { Limit = 0 }, new PlayServRoomBrowseQuery { Limit = 201 },
                new PlayServRoomBrowseQuery { PlacementState = "unknown" },
                new PlayServRoomBrowseQuery { Region = new string('r', 33) },
                new PlayServRoomBrowseQuery { Attributes = Filters(5) },
                new PlayServRoomBrowseQuery { Attributes = new Dictionary<string, string> { [""] = "x" } },
                new PlayServRoomBrowseQuery { Attributes = new Dictionary<string, string> { ["map"] = null } }
            };
            foreach (var query in queries)
                Assert.That(() => client.BrowseRoomsAsync("arena", query, default).GetAwaiter().GetResult(), Throws.InstanceOf<ArgumentException>());
            Assert.That(resolutions, Is.Zero);
            Assert.That(http.Requests, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ab")]
        [TestCase("Arena")]
        [TestCase("bad/slug")]
        [TestCase("arena?x")]
        [TestCase("arena\n")]
        public void Both_operations_validate_room_type_before_io(string slug)
        {
            var http = new HttpFixture(); var client = Create(http);
            Assert.Throws<ArgumentException>(() => client.BrowseRoomsAsync(slug, null, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.JoinRoomAsync(slug, "room", default).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" room")]
        [TestCase("../room")]
        [TestCase("room/name")]
        [TestCase("room?x")]
        [TestCase("room\n")]
        public void Join_validates_room_name_before_io(string room)
        {
            var http = new HttpFixture();
            Assert.Throws<ArgumentException>(() => Create(http).JoinRoomAsync("arena", room, default).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void Identifier_length_boundaries_match_contract()
        {
            var http = new HttpFixture(); http.Enqueue(Reservation);
            var client = Create(http);
            client.JoinRoomAsync(new string('a', 50), new string('b', 64), default).GetAwaiter().GetResult();
            Assert.Throws<ArgumentException>(() => client.JoinRoomAsync(new string('a', 51), "room", default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.JoinRoomAsync("arena", new string('b', 65), default).GetAwaiter().GetResult());
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Direct_join_posts_no_body_and_exposes_verbatim_connect_and_immutable_metadata()
        {
            var http = new HttpFixture(); http.Enqueue(Reservation);
            var result = Create(http).JoinRoomAsync("arena", "room:one", default).GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(PlayServMatchStatus.Matched));
            Assert.That(result.Reservation.RoomName, Is.EqualTo("room:one"));
            Assert.That(result.Reservation.ReservationToken, Is.EqualTo("rsv_secret"));
            Assert.That(result.Reservation.Connect.Host, Is.EqualTo("Game.Example.com"));
            Assert.That(result.Reservation.Connect.Port, Is.EqualTo(7777));
            Assert.That(result.Reservation.Connect.Transport, Is.EqualTo("udp"));
            Assert.That(result.Reservation.Connect.ConnectString, Is.EqualTo("opaque ? text"));
            Assert.That(result.Reservation.Connect.Region, Is.EqualTo("eu"));
            Assert.That(result.Reservation.Region, Is.EqualTo("eu"));
            Assert.That(result.Reservation.Attributes["map"], Is.EqualTo("arena"));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object>)result.Reservation.Attributes).Clear());
            var nested = (IReadOnlyDictionary<string, object>)result.Reservation.Attributes["nested"];
            Assert.Throws<NotSupportedException>(() => ((IList<object>)nested["flags"]).Clear());
            Assert.That(http.Requests, Has.Count.EqualTo(1));
            AssertPlayerRequest(http.Requests[0], "POST");
            Assert.That(http.Requests[0].RelativePath, Is.EqualTo("rooms/arena/room%3Aone:join"));
        }

        [Test]
        public void Ttl_counts_from_response_not_request_or_wall_clock_and_find_is_additive()
        {
            double seconds = 100;
            var wall = DateTimeOffset.UtcNow;
            var http = new HttpFixture();
            http.Handler = (_, __) => { seconds += 30; return Task.FromResult(Response(Reservation)); };
            var client = Create(http, monotonic: () => seconds, utc: () => wall);
            var reservation = client.JoinRoomAsync("arena", "room", default).GetAwaiter().GetResult().Reservation;
            Assert.That(reservation.ExpiresIn, Is.EqualTo(10));
            Assert.That(reservation.RemainingLifetime, Is.EqualTo(TimeSpan.FromSeconds(10)));
            wall = wall.AddDays(-10);
            seconds += 2.5;
            Assert.That(reservation.RemainingLifetime, Is.EqualTo(TimeSpan.FromSeconds(7.5)));
            wall = wall.AddYears(20);
            seconds += 100;
            Assert.That(reservation.RemainingLifetime, Is.EqualTo(TimeSpan.Zero));
            var find = client.FindMatchAsync("arena", null, 0, 0, default).GetAwaiter().GetResult();
            Assert.That(find.Reservation.Connect.Host, Is.EqualTo("Game.Example.com"));
            Assert.That(find.Reservation.RemainingLifetime, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(http.Requests[1].RelativePath, Is.EqualTo("matchmaking/arena/find"));
        }

        [Test]
        public void Zero_lifetime_null_connect_and_legacy_find_without_ttl_are_valid()
        {
            var http = new HttpFixture();
            http.Enqueue("{\"room_name\":\"r\",\"reservation_token\":\"rsv_x\",\"expires_at\":\"2000-01-01T00:00:00Z\",\"expires_in\":0,\"connect\":null}");
            http.Enqueue("{\"room_name\":\"r\",\"reservation_token\":\"rsv_x\",\"expires_at\":\"2000-01-01T00:00:00Z\"}");
            var client = Create(http);
            var join = client.JoinRoomAsync("arena", "r", default).GetAwaiter().GetResult();
            Assert.That(join.Reservation.RemainingLifetime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(join.Reservation.Connect, Is.Null);
            var find = client.FindMatchAsync("arena", null, 0, 0, default).GetAwaiter().GetResult();
            Assert.That(find.IsMatched, Is.True);
            Assert.That(find.Reservation.ExpiresIn, Is.Null);
            Assert.That(find.Reservation.RemainingLifetime, Is.Null);
        }

        [TestCase("\"expires_in\":-1")]
        [TestCase("\"expires_in\":1.5")]
        [TestCase("\"expires_in\":\"10\"")]
        [TestCase("\"expires_in\":null")]
        [TestCase("\"expires_in\":2147483648")]
        [TestCase("\"different_field\":10")]
        public void Join_requires_an_integer_nonnegative_ttl(string replacement)
        {
            var http = new HttpFixture(); http.Enqueue(Reservation.Replace("\"expires_in\":10", replacement));
            var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).JoinRoomAsync("arena", "room", default).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
        }

        [TestCase("")]
        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("not-json rsv_secret")]
        [TestCase("{\"status\":\"searching\",\"expires_in\":10}")]
        [TestCase("{\"status\":\"not_found\",\"expires_in\":10}")]
        [TestCase("{\"status\":\"bot\",\"expires_in\":10}")]
        [TestCase("{\"status\":\"rsv_secret\",\"expires_in\":10}")]
        public void Join_rejects_incomplete_and_nonmatched_success_without_retry(string body)
        {
            var http = new HttpFixture(); http.Enqueue(body);
            var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).JoinRoomAsync("arena", "room", default).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
            Assert.That(error.ToString(), Does.Not.Contain("rsv_secret"));
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("{\"data\":[]}")]
        [TestCase("{\"data\":null,\"page\":{}}")]
        [TestCase("{\"data\":[{}],\"page\":{}}")]
        [TestCase("{\"data\":[],\"page\":{\"has_more\":true}}")]
        public void Browse_rejects_malformed_success(string body)
        {
            var http = new HttpFixture(); http.Enqueue(body);
            var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).BrowseRoomsAsync("arena", null, default).GetAwaiter().GetResult());
            Assert.That(error.Operation, Is.EqualTo(PlayServMatchmakingOperation.BrowseRooms));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
        }

        [TestCase("room_not_found", 404, PlayServRoomFailureCode.RoomNotFound, false)]
        [TestCase("room_type_not_found", 404, PlayServRoomFailureCode.RoomTypeNotFound, false)]
        [TestCase("room_full", 409, PlayServRoomFailureCode.RoomFull, false)]
        [TestCase("room_closed", 409, PlayServRoomFailureCode.RoomClosed, false)]
        [TestCase("room_refused", 403, PlayServRoomFailureCode.RoomRefused, false)]
        [TestCase("room_unreachable", 503, PlayServRoomFailureCode.RoomUnreachable, true)]
        [TestCase("future_room_code", 409, PlayServRoomFailureCode.Unknown, false)]
        public void Refusals_preserve_codes_detail_and_retryability_for_both_http_error_forms(
            string code, int status, PlayServRoomFailureCode expected, bool retryable)
        {
            foreach (var throws in new[] { false, true })
            {
                var body = "{\"code\":\"" + code + "\",\"detail\":\"CombatState\"}";
                var http = new HttpFixture();
                http.Handler = (_, __) => throws
                    ? throw new PlayServRuntimeHttpException("HTTP error", status, body, code, false)
                    : Task.FromResult(Response(body, status));
                var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).JoinRoomAsync("arena", "room", default).GetAwaiter().GetResult());
                Assert.That(error.Operation, Is.EqualTo(PlayServMatchmakingOperation.JoinRoom));
                Assert.That(error.FunctionSlug, Is.EqualTo("arena"));
                Assert.That(error.RoomFailureCode, Is.EqualTo(expected));
                Assert.That(error.UnifiedError.SourceCode, Is.EqualTo(code));
                Assert.That(error.UnifiedError.HttpStatus, Is.EqualTo(status));
                Assert.That(error.UnifiedError.Retryable, Is.EqualTo(retryable));
                Assert.That(error.UnifiedError.Message, Is.EqualTo("CombatState"));
                Assert.That(http.Requests, Has.Count.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Error_diagnostics_never_retain_credentials_or_raw_response(bool throws)
        {
            var body = "{\"code\":\"room_refused\",\"detail\":\"pk_public Bearer sk_private a.b.c rsv_secret player.jwt.value\",\"reservation_token\":\"opaque-ticket-secret\"}";
            var http = new HttpFixture();
            http.Handler = (_, __) => throws
                ? throw new PlayServRuntimeHttpException(body, 403, body, "room_refused", false, new Exception(body))
                : Task.FromResult(Response(body, 403));
            var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).JoinRoomAsync("arena", "room", default).GetAwaiter().GetResult());
            foreach (var secret in new[] { "pk_public", "sk_private", "a.b.c", "rsv_secret", "player.jwt.value", "opaque-ticket-secret" })
            {
                Assert.That(error.ToString(), Does.Not.Contain(secret));
                Assert.That(error.UnifiedError.RawDetails, Does.Not.Contain(secret));
            }
        }

        [Test]
        public void Cancellation_before_credentials_and_during_http_is_not_wrapped_or_retried()
        {
            var http = new HttpFixture(); var client = Create(http);
            using var ct = new CancellationTokenSource(); ct.Cancel();
            Assert.Throws<OperationCanceledException>(() => client.BrowseRoomsAsync("arena", null, ct.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => client.JoinRoomAsync("arena", "room", ct.Token).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
            using var live = new CancellationTokenSource();
            http.Handler = (_, token) => { live.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(Response(EmptyPage)); };
            Assert.Throws<OperationCanceledException>(() => client.BrowseRoomsAsync("arena", null, live.Token).GetAwaiter().GetResult());
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Cancellation_during_token_resolution_does_not_send_and_missing_session_is_configuration_error()
        {
            var http = new HttpFixture();
            using var ct = new CancellationTokenSource();
            var token = new TaskCompletionSource<string>();
            var client = Create(http, token: _ => token.Task);
            var pending = client.JoinRoomAsync("arena", "room", ct.Token);
            ct.Cancel(); token.SetResult("player.jwt.value");
            var error = Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.That(error.CancellationToken, Is.EqualTo(ct.Token));
            Assert.Throws<InvalidOperationException>(() => Create(http, token: _ => Task.FromResult<string>(null))
                .BrowseRoomsAsync("arena", null, default).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void Browse_http_error_preserves_problem_and_never_falls_back_to_legacy_route()
        {
            var http = new HttpFixture();
            http.Handler = (_, __) => Task.FromResult(Response("{\"code\":\"room_type_not_found\",\"detail\":\"No room configuration\",\"retryable\":true}", 404));
            var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).BrowseRoomsAsync("arena", null, default).GetAwaiter().GetResult());
            Assert.That(error.Operation, Is.EqualTo(PlayServMatchmakingOperation.BrowseRooms));
            Assert.That(error.RoomFailureCode, Is.EqualTo(PlayServRoomFailureCode.RoomTypeNotFound));
            Assert.That(error.UnifiedError.Message, Is.EqualTo("No room configuration"));
            Assert.That(error.UnifiedError.Retryable, Is.False);
            Assert.That(http.Requests, Has.Count.EqualTo(1));
            Assert.That(http.Requests[0].RelativePath, Does.StartWith("rooms/arena:browse?"));
        }

        [Test]
        public void Malformed_endpoint_is_not_returned_or_copied_to_exception()
        {
            var http = new HttpFixture(); http.Enqueue(Reservation.Replace("\"port\":7777", "\"port\":\"rsv_secret\""));
            var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).JoinRoomAsync("arena", "room", default).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
            Assert.That(error.ToString(), Does.Not.Contain("rsv_secret"));
            Assert.That(error.InnerException, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Timeout_and_network_failure_stay_normalized_without_automatic_retry(bool timeout)
        {
            var http = new HttpFixture();
            http.Handler = (_, __) => timeout ? throw new OperationCanceledException("rsv_secret") : throw new Exception("pk_private");
            var error = Assert.Throws<PlayServMatchmakingException>(() => Create(http).JoinRoomAsync("arena", "room", default).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(timeout ? PlayServErrorCode.Timeout : PlayServErrorCode.Network));
            Assert.That(error.UnifiedError.Retryable, Is.True);
            Assert.That(error.ToString(), Does.Not.Contain("rsv_secret").And.Not.Contain("pk_private"));
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Public_signatures_compile_and_old_operation_values_are_stable()
        {
            Func<string, PlayServRoomBrowseQuery, CancellationToken, Task<PlayServRoomBrowsePage>> browse = PlayServMatchmaking.BrowseRoomsAsync;
            Func<string, string, CancellationToken, Task<PlayServMatchResult>> join = PlayServMatchmaking.JoinRoomAsync;
            Assert.That(browse, Is.Not.Null); Assert.That(join, Is.Not.Null);
            Assert.That((int)PlayServMatchmakingOperation.FindMatch, Is.EqualTo(0));
            Assert.That((int)PlayServMatchmakingOperation.JoinGame, Is.EqualTo(1));
            Assert.That((int)PlayServMatchmakingOperation.LaunchServer, Is.EqualTo(2));
        }

        private static void AssertPlayerRequest(PlayServRuntimeDataRequest request, string method)
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.ClientToken, Is.EqualTo("pk_public"));
            Assert.That(request.BearerToken, Is.EqualTo("player.jwt.value"));
            Assert.That(request.RequiresClientToken, Is.True);
            Assert.That(request.JsonBody, Is.Null);
            Assert.That(request.TimeoutSeconds, Is.EqualTo(10));
        }

        private static Dictionary<string, string> Filters(int count)
        {
            var result = new Dictionary<string, string>();
            for (var i = 0; i < count; i++) result.Add("key" + i, "value");
            return result;
        }

        private static PlayServMatchmakingClient Create(HttpFixture http, Func<double> monotonic = null,
            Func<DateTimeOffset> utc = null, Func<CancellationToken, Task<string>> token = null) =>
            new PlayServMatchmakingClient(new PlayServSettings {
                ClientToken = "pk_public", RuntimeTokenProvider = new PlayServDelegateRuntimeTokenProvider(token ?? (_ => Task.FromResult("player.jwt.value")))
            }, http, new NewtonsoftJsonCodec(), utcNow: utc, monotonicSeconds: monotonic);

        private static PlayServRuntimeDataResponse Response(string body, int status = 200) => new PlayServRuntimeDataResponse(status, body, null, null);

        private sealed class HttpFixture : IPlayServRuntimeHttpClient
        {
            private readonly Queue<string> _bodies = new Queue<string>();
            public readonly List<PlayServRuntimeDataRequest> Requests = new List<PlayServRuntimeDataRequest>();
            public Func<PlayServRuntimeDataRequest, CancellationToken, Task<PlayServRuntimeDataResponse>> Handler;
            public void Enqueue(string body) => _bodies.Enqueue(body);
            public Task<PlayServRuntimeDataResponse> SendDataAsync(PlayServRuntimeDataRequest request, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Requests.Add(request);
                return Handler != null ? Handler(request, ct) : Task.FromResult(Response(_bodies.Dequeue()));
            }
            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> SignInAnonAsync(string clientToken, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerRefreshResponseDto> RefreshAsync(string clientToken, string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> LoginExternalAsync(string clientToken, PlayerExternalLoginRequestDto request, string playerAccessToken = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task SignOutAsync(string clientToken, string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
        }
    }
}
