using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Matchmaking;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServRoomHostTests
    {
        private const string Reservation = "{\"status\":\"matched\",\"room_name\":\"minted-42\",\"reservation_token\":\"rsv_private\",\"expires_at\":\"2000-01-01T00:00:00Z\",\"expires_in\":10,\"connect\":{\"host\":\"game.example\",\"port\":443,\"transport\":\"wss\",\"connect_string\":\"opaque\"},\"attributes\":{\"title\":\"Accepted\",\"bots\":2},\"region\":\"eu\"}";
        private static readonly NewtonsoftJsonCodec Json = new NewtonsoftJsonCodec();

        [Test]
        public void Host_sends_one_player_request_and_returns_authoritative_reservation()
        {
            var http = new HttpFixture(); var now = 12d;
            var result = Host(Create(http, clock: () => now), Request(new { title = "Requested", bots = 8 }, "eu")).GetAwaiter().GetResult();
            Assert.That(http.Requests, Has.Count.EqualTo(1));
            var sent = http.Requests[0];
            Assert.That(sent.Method, Is.EqualTo("POST"));
            Assert.That(sent.RelativePath, Is.EqualTo("rooms/tank-room:host"));
            Assert.That(sent.ClientToken, Is.EqualTo("pk_public"));
            Assert.That(sent.BearerToken, Is.EqualTo("player.jwt.secret"));
            Assert.That(sent.RequiresClientToken, Is.True);
            Assert.That(sent.TimeoutSeconds, Is.EqualTo(45));
            Assert.That(sent.JsonBody, Is.EqualTo("{\"attributes\":{\"title\":\"Requested\",\"bots\":8},\"region\":\"eu\"}"));
            Assert.That(result.Status, Is.EqualTo(PlayServMatchStatus.Matched));
            Assert.That(result.Reservation.RoomName, Is.EqualTo("minted-42"));
            Assert.That(result.Reservation.Connect.Transport, Is.EqualTo("wss"));
            Assert.That(result.Reservation.ReservationToken, Is.EqualTo("rsv_private"));
            Assert.That(result.Reservation.Attributes["title"], Is.EqualTo("Accepted"));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object>)result.Reservation.Attributes)["title"] = "modified");
            now += 3;
            Assert.That(result.Reservation.RemainingLifetime, Is.EqualTo(TimeSpan.FromSeconds(7)));
            now += 20;
            Assert.That(result.Reservation.RemainingLifetime, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void Host_omits_absent_fields_preserves_empty_object_and_does_not_poll_null_connect()
        {
            var http = new HttpFixture { Body = Reservation.Replace("\"connect\":", "\"unused\":").Replace("\"expires_in\":10", "\"expires_in\":0") };
            var client = Create(http);
            var result = Host(client, Request()).GetAwaiter().GetResult();
            Assert.That(http.Requests[0].JsonBody, Is.EqualTo("{}"));
            Assert.That(result.Reservation.Connect, Is.Null);
            Assert.That(result.Reservation.RemainingLifetime, Is.EqualTo(TimeSpan.Zero));
            Host(client, Request(new Dictionary<string, object>())).GetAwaiter().GetResult();
            Assert.That(http.Requests[1].JsonBody, Is.EqualTo("{\"attributes\":{}}"));
            Assert.That(http.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public void Host_snapshots_request_and_options_before_token_resolution()
        {
            var http = new HttpFixture();
            var token = new TaskCompletionSource<string>();
            var attributes = new Dictionary<string, object> { ["title"] = "original" };
            var request = Request(attributes, "eu"); var options = Options(45);
            var previous = SynchronizationContext.Current;
            Task<PlayServMatchResult> pending;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                pending = Host(Create(http, token: _ => token.Task), request, options);
                attributes["title"] = "changed";
                request.Region = "us"; request.FunctionSlug = "changed"; options.Timeout = TimeSpan.Zero;
                token.SetResult("player.jwt.secret");
                pending.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            Assert.That(http.Requests[0].RelativePath, Is.EqualTo("rooms/tank-room:host"));
            Assert.That(http.Requests[0].JsonBody, Is.EqualTo("{\"attributes\":{\"title\":\"original\"},\"region\":\"eu\"}"));
        }

        [TestCase(false)] [TestCase(true)]
        public void Host_enforces_utf8_attributes_boundary(bool unicode)
        {
            var http = new HttpFixture(); var client = Create(http);
            var prefix = unicode ? "🎮Ї" : "";
            var padding = 2048 - Encoding.UTF8.GetByteCount(Json.Serialize(new { title = prefix }));
            var accepted = new { title = prefix + new string('x', padding) };
            Assert.That(Encoding.UTF8.GetByteCount(Json.Serialize(accepted)), Is.EqualTo(2048));
            Host(client, Request(accepted)).GetAwaiter().GetResult();
            Assert.Throws<ArgumentException>(() => Host(client, Request(new { title = accepted.title + "x" })).GetAwaiter().GetResult());
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Host_rejects_invalid_attributes_identifiers_and_budget_before_credentials()
        {
            var http = new HttpFixture(); var resolutions = 0;
            var client = Create(http, token: _ => { resolutions++; return Task.FromResult("player.jwt.secret"); });
            foreach (var value in new object[] { 1, "{}", new[] { 1 }, new { nested = new { n = 1 } }, new { nested = new[] { 1 } }, new ExplodingAttributes() })
            {
                var error = Assert.Throws<ArgumentException>(() => Host(client, Request(value)).GetAwaiter().GetResult());
                Assert.That(error.ToString(), Does.Not.Contain("secret_attribute"));
            }
            foreach (var slug in new[] { null, "", "ab", "Arena", "bad/slug", "arena\n", new string('a', 51) })
            {
                var request = Request(); request.FunctionSlug = slug;
                Assert.Throws<ArgumentException>(() => Host(client, request).GetAwaiter().GetResult());
            }
            Assert.Throws<ArgumentException>(() => Host(client, Request(region: new string('r', 33))).GetAwaiter().GetResult());
            foreach (var seconds in new[] { 0d, -1d, (double)int.MaxValue })
                Assert.Throws<ArgumentOutOfRangeException>(() => Host(client, Request(), Options(seconds)).GetAwaiter().GetResult());
            Assert.Throws<ArgumentNullException>(() => Host(client, null).GetAwaiter().GetResult());
            Assert.That(resolutions, Is.Zero); Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void Host_preserves_serialization_names_and_primitive_types()
        {
            var http = new HttpFixture();
            Host(Create(http), Request(new NamedAttributes())).GetAwaiter().GetResult();
            Assert.That(http.Requests[0].JsonBody, Is.EqualTo("{\"attributes\":{\"max_players\":4,\"open\":true,\"ratio\":1.5}}"));
        }

        [Test]
        public void Host_preserves_date_like_strings_in_requested_and_authoritative_attributes()
        {
            const string stamp = "2026-09-17T12:30:00+02:00";
            var http = new HttpFixture { Body = Reservation.Replace("Accepted", stamp) };
            var result = Host(Create(http), Request(new { title = stamp })).GetAwaiter().GetResult();
            Assert.That(http.Requests[0].JsonBody, Is.EqualTo("{\"attributes\":{\"title\":\"" + stamp + "\"}}"));
            Assert.That(result.Reservation.Attributes["title"], Is.TypeOf<string>().And.EqualTo(stamp));
        }

        [TestCase("$type")] [TestCase("$id")] [TestCase("$ref")] [TestCase("$values")]
        public void Host_preserves_metadata_shaped_attribute_keys(string key)
        {
            var attributes = new Dictionary<string, object> { [key] = "literal", ["title"] = "Friends" };
            var encoded = Json.Serialize(attributes);
            var http = new HttpFixture { Body = Reservation.Replace("{\"title\":\"Accepted\",\"bots\":2}", encoded) };
            var result = Host(Create(http), Request(attributes)).GetAwaiter().GetResult();
            Assert.That(http.Requests[0].JsonBody, Is.EqualTo("{\"attributes\":" + encoded + "}"));
            Assert.That(result.Reservation.Attributes.ContainsKey(key), Is.True);
            Assert.That(result.Reservation.Attributes[key], Is.EqualTo("literal"));
        }

        [TestCase("room_quota_exceeded", 429, "RoomQuotaExceeded")]
        [TestCase("room_host_unavailable", 503, "RoomHostUnavailable")]
        [TestCase("room_refused", 403, "RoomRefused")]
        [TestCase("room_unreachable", 503, "RoomUnreachable")]
        [TestCase("room_host_capacity_exhausted", 429, "RoomHostCapacityExhausted")]
        [TestCase("region_unavailable", 503, "RegionUnavailable")]
        [TestCase("future_room_error", 503, "Unknown")]
        [TestCase("unauthenticated", 401, "Unknown")]
        public void Host_preserves_refusals_detail_and_retry_after_on_both_http_paths(string code, int status, string expected)
        {
            foreach (var throwing in new[] { false, true })
            {
                var http = new HttpFixture();
                var headers = new Dictionary<string, string> { ["Retry-After"] = "7" };
                var body = Json.Serialize(new { code, detail = "Setting bots was refused" });
                http.Handler = (_, __) => throwing
                    ? throw new PlayServRuntimeHttpException("unsafe", status, body, code, false, null, null, null, null, headers)
                    : Task.FromResult(new PlayServRuntimeDataResponse(status, body, null, null, headers: headers));
                var error = Assert.Throws<PlayServMatchmakingException>(() => Host(Create(http), Request()).GetAwaiter().GetResult());
                Assert.That(error.Operation.ToString(), Is.EqualTo("HostRoom"));
                Assert.That(error.RoomFailureCode.ToString(), Is.EqualTo(expected));
                Assert.That(error.UnifiedError.SourceCode, Is.EqualTo(code));
                Assert.That(error.UnifiedError.Message, Is.EqualTo("Setting bots was refused"));
                Assert.That(error.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(7)));
                Assert.That(error.InnerException, Is.Null);
                Assert.That(http.Requests, Has.Count.EqualTo(1));
            }
        }

        [TestCase("status", "\"searching\"")]
        [TestCase("status", "null")]
        [TestCase("status", "\"MATCHED\"")]
        [TestCase("room_name", "null")]
        [TestCase("room_name", "\"bad/name\"")]
        [TestCase("reservation_token", "\"\"")]
        [TestCase("reservation_token", "123")]
        [TestCase("expires_at", "null")]
        [TestCase("expires_at", "\"not a date\"")]
        [TestCase("expires_in", "null")]
        [TestCase("expires_in", "-1")]
        [TestCase("expires_in", "1.5")]
        [TestCase("expires_in", "\"10\"")]
        [TestCase("expires_in", "2147483648")]
        public void Host_rejects_malformed_reservations_without_coercion(string field, string value)
        {
            var plain = (Dictionary<string, object>)Json.ParseToPlainValue(Reservation);
            plain[field] = Json.ParseToPlainValue(value);
            AssertInvalidResponse(Json.Serialize(plain));
            plain.Remove(field);
            AssertInvalidResponse(Json.Serialize(plain));
        }

        [TestCase("")] [TestCase("{")] [TestCase("null")] [TestCase("[]")] [TestCase("{}")]
        public void Host_rejects_malformed_body(string body) => AssertInvalidResponse(body);

        [Test]
        public void Host_redacts_credentials_in_refusal_diagnostics()
        {
            var http = new HttpFixture { Handler = (_, __) => Task.FromResult(new PlayServRuntimeDataResponse(403,
                "{\"code\":\"room_refused\",\"detail\":\"Rejected pk_public player.jwt.secret rsv_private Bearer sk_private\"}", null, null)) };
            var error = Assert.Throws<PlayServMatchmakingException>(() => Host(Create(http), Request()).GetAwaiter().GetResult());
            Assert.That(error.ToString(), Does.Not.Contain("pk_public").And.Not.Contain("player.jwt.secret").And.Not.Contain("rsv_private").And.Not.Contain("sk_private"));
        }

        [Test]
        public void Host_precancellation_and_missing_player_token_never_send_http()
        {
            var http = new HttpFixture(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var error = Assert.Throws<OperationCanceledException>(() => Host(Create(http), Request(), ct: canceled.Token).GetAwaiter().GetResult());
            Assert.That(error.CancellationToken, Is.EqualTo(canceled.Token));
            Assert.Throws<InvalidOperationException>(() => Host(Create(http, token: _ => Task.FromResult<string>(null)), Request()).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [TestCase(false)] [TestCase(true)]
        public void Host_token_provider_failures_do_not_escape_with_credentials(bool faulted)
        {
            foreach (var failure in new Exception[] {
                new InvalidOperationException("pk_private secret_attribute"),
                new PlayServRuntimeHttpException("Bearer sk_private", 503, "secret_attribute", "private", false),
                new OperationCanceledException("player.jwt.secret") })
            {
                var http = new HttpFixture();
                var client = Create(http, token: _ => faulted ? Task.FromException<string>(failure) : throw failure);
                var error = Assert.Throws<PlayServMatchmakingException>(() => Host(client, Request()).GetAwaiter().GetResult());
                Assert.That(error.Operation, Is.EqualTo(PlayServMatchmakingOperation.HostRoom));
                Assert.That(error.ToString(), Does.Not.Contain("pk_private").And.Not.Contain("sk_private")
                    .And.Not.Contain("player.jwt.secret").And.Not.Contain("secret_attribute"));
                Assert.That(error.InnerException, Is.Null);
                Assert.That(http.Requests, Is.Empty);
            }
        }

        [Test]
        public void Host_budget_includes_auth_and_checks_monotonic_deadline_after_http()
        {
            var now = 0d; var http = new HttpFixture();
            Host(Create(http, clock: () => now, token: _ => { now = 4; return Task.FromResult("player.jwt.secret"); }), Request(), Options(12)).GetAwaiter().GetResult();
            Assert.That(http.Requests[0].TimeoutSeconds, Is.EqualTo(8));
            http.Requests.Clear(); now = 0;
            var authError = Assert.Throws<PlayServMatchmakingException>(() => Host(Create(http, clock: () => now,
                token: _ => { now = 46; return Task.FromResult("player.jwt.secret"); }), Request()).GetAwaiter().GetResult());
            Assert.That(authError.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(http.Requests, Is.Empty);
            now = 0; http.Handler = (_, __) => { now = 46; return Task.FromResult(Response(Reservation)); };
            var httpError = Assert.Throws<PlayServMatchmakingException>(() => Host(Create(http, clock: () => now), Request()).GetAwaiter().GetResult());
            Assert.That(httpError.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Host_bounds_noncooperative_auth_and_http_and_observes_late_completion()
        {
            foreach (var auth in new[] { true, false })
            {
                var http = new HttpFixture(); var token = new TaskCompletionSource<string>();
                var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
                http.Handler = (_, __) => response.Task;
                var pending = Host(Create(http, token: _ => auth ? token.Task : Task.FromResult("player.jwt.secret")), Request(), Options(.05));
                yield return Wait(pending);
                var error = Assert.Throws<PlayServMatchmakingException>(() => pending.GetAwaiter().GetResult());
                Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
                token.TrySetResult("player.jwt.secret"); response.TrySetResult(Response(Reservation));
                yield return null;
                Assert.That(http.Requests, Has.Count.EqualTo(auth ? 0 : 1));
            }
        }

        [UnityTest]
        public IEnumerator Host_cancellation_terminates_noncooperative_provider_without_retry()
        {
            foreach (var auth in new[] { true, false })
            {
                var http = new HttpFixture(); var token = new TaskCompletionSource<string>();
                var response = new TaskCompletionSource<PlayServRuntimeDataResponse>();
                http.Handler = (_, __) => response.Task;
                using var cancel = new CancellationTokenSource();
                var pending = Host(Create(http, token: _ => auth ? token.Task : Task.FromResult("player.jwt.secret")), Request(), ct: cancel.Token);
                cancel.Cancel(); yield return Wait(pending);
                var error = Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
                Assert.That(error.CancellationToken, Is.EqualTo(cancel.Token));
                token.TrySetResult("player.jwt.secret"); response.TrySetResult(Response(Reservation));
                yield return null;
                Assert.That(http.Requests, Has.Count.EqualTo(auth ? 0 : 1));
            }
        }

        [UnityTest]
        public IEnumerator Host_resumes_on_unity_context_after_background_token_resolution()
        {
            Assert.That(SynchronizationContext.Current, Is.Not.Null);
            var thread = Thread.CurrentThread.ManagedThreadId; var sentOn = 0;
            var http = new HttpFixture(); var token = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            http.Handler = (_, __) => { sentOn = Thread.CurrentThread.ManagedThreadId; return Task.FromResult(Response(Reservation)); };
            var pending = Host(Create(http, token: _ => token.Task), Request());
            _ = Task.Run(() => token.SetResult("player.jwt.secret"));
            yield return Wait(pending);
            Assert.That(pending.GetAwaiter().GetResult().Reservation, Is.Not.Null);
            Assert.That(sentOn, Is.EqualTo(thread));
        }

        private static IEnumerator Wait(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Host task did not finish inside the test deadline.");
        }

        private static void AssertInvalidResponse(string body)
        {
            var http = new HttpFixture { Body = body };
            var error = Assert.Throws<PlayServMatchmakingException>(() => Host(Create(http), Request()).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
            Assert.That(error.InnerException, Is.Null);
            Assert.That(error.ToString(), Does.Not.Contain("rsv_private"));
            Assert.That(http.Requests, Has.Count.EqualTo(1));
        }

        private static PlayServHostRoomRequest Request(object attributes = null, string region = null) =>
            new PlayServHostRoomRequest { FunctionSlug = "tank-room", Attributes = attributes, Region = region };

        private static PlayServRoomHostOptions Options(double seconds) => new PlayServRoomHostOptions { Timeout = TimeSpan.FromSeconds(seconds) };

        private static Task<PlayServMatchResult> Host(PlayServMatchmakingClient client, PlayServHostRoomRequest request,
            PlayServRoomHostOptions options = null, CancellationToken ct = default) => client.HostRoomAsync(request, options, ct);

        private sealed class NamedAttributes
        {
            [PlayServJsonName("max_players")] public int MaxPlayers = 4;
            public bool open = true;
            public double ratio = 1.5;
        }
        private sealed class ExplodingAttributes { public string Value => throw new Exception("secret_attribute"); }

        private static PlayServMatchmakingClient Create(HttpFixture http, Func<double> clock = null, Func<CancellationToken, Task<string>> token = null) =>
            new PlayServMatchmakingClient(new PlayServSettings { ClientToken = "pk_public",
                RuntimeTokenProvider = new PlayServDelegateRuntimeTokenProvider(token ?? (_ => Task.FromResult("player.jwt.secret"))) },
                http, Json, monotonicSeconds: clock);

        private static PlayServRuntimeDataResponse Response(string body) => new PlayServRuntimeDataResponse(200, body, null, null);

        private sealed class HttpFixture : IPlayServRuntimeHttpClient
        {
            public string Body = Reservation;
            public readonly List<PlayServRuntimeDataRequest> Requests = new List<PlayServRuntimeDataRequest>();
            public Func<PlayServRuntimeDataRequest, CancellationToken, Task<PlayServRuntimeDataResponse>> Handler;
            public Task<PlayServRuntimeDataResponse> SendDataAsync(PlayServRuntimeDataRequest request, CancellationToken ct = default)
            { ct.ThrowIfCancellationRequested(); Requests.Add(request); return Handler == null ? Task.FromResult(Response(Body)) : Handler(request, ct); }
            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> SignInAnonAsync(string clientToken, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerRefreshResponseDto> RefreshAsync(string clientToken, string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> LoginExternalAsync(string clientToken, PlayerExternalLoginRequestDto request, string playerAccessToken = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task SignOutAsync(string clientToken, string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
        }
    }
}
