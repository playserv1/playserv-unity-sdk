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
    public sealed class PlayServMatchmakingTests
    {
        [TestCase(-1, 10)]
        [TestCase(0, 10)]
        [TestCase(1, 6)]
        [TestCase(20_000, 25)]
        [TestCase(25_000, 30)]
        public void Find_match_timeout_comes_from_wait_ms_plus_network_margin(
            int waitMs,
            int expectedTimeoutSeconds)
        {
            Assert.That(
                PlayServMatchmakingClient.ResolveFindMatchTimeoutSeconds(waitMs),
                Is.EqualTo(expectedTimeoutSeconds));
        }

        [Test]
        public void Join_game_allows_a_response_after_the_old_ten_second_boundary()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue(
                "{\"status\":\"matched\",\"room_name\":\"tanks-delayed\",\"reservation_token\":\"rsv_delayed\",\"expires_at\":\"2026-08-19T00:00:00Z\"}",
                simulatedDurationSeconds: 15);
            var client = CreateClient(fake);

            var result = client.JoinGameAsync("tank-room", null, null, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServJoinGameStatus.Matched));
            Assert.That(result.Reservation.RoomName, Is.EqualTo("tanks-delayed"));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].TimeoutSeconds, Is.EqualTo(25));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"wait_ms\":20000"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"search_age_ms\":0"));
        }

        [Test]
        public void Find_match_uses_the_same_wait_for_wire_and_deadline()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"status\":\"not_found\"}");
            var client = CreateClient(fake);

            var result = client.FindMatchAsync("arena", "ranked", 12_345, 678, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServMatchStatus.NotFound));
            var request = fake.Requests[0];
            Assert.That(request.RelativePath, Is.EqualTo("matchmaking/arena/find"));
            Assert.That(request.ClientToken, Is.EqualTo("pk_public"));
            Assert.That(request.BearerToken, Is.EqualTo("player.jwt.value"));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(18));
            Assert.That(request.JsonBody, Does.Contain("\"matchmaker\":\"ranked\""));
            Assert.That(request.JsonBody, Does.Contain("\"wait_ms\":12345"));
            Assert.That(request.JsonBody, Does.Contain("\"search_age_ms\":678"));
        }

        [Test]
        public void Find_request_serializes_typed_lobby_parameters_and_all_player_fields()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"status\":\"not_found\"}");
            var client = CreateClient(fake);

            client.FindMatchAsync(new PlayServFindMatchRequest
                {
                    FunctionSlug = "arena",
                    Matchmaker = "ranked",
                    Parameters = new LobbyParameters { mode = "duo", skill = 1700 },
                    WaitMs = 4_321,
                    SearchAgeMs = 9_876
                }, default)
                .GetAwaiter()
                .GetResult();

            var request = fake.Requests[0];
            Assert.That(request.JsonBody, Does.Contain("\"matchmaker\":\"ranked\""));
            Assert.That(request.JsonBody, Does.Contain("\"params\":{\"mode\":\"duo\",\"skill\":1700}"));
            Assert.That(request.JsonBody, Does.Contain("\"wait_ms\":4321"));
            Assert.That(request.JsonBody, Does.Contain("\"search_age_ms\":9876"));
            Assert.That(request.JsonBody, Does.Not.Contain("player_id"));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(10));
        }

        [Test]
        public void Find_request_accepts_dictionary_lobby_parameters()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"status\":\"not_found\"}");
            var client = CreateClient(fake);

            client.FindMatchAsync(new PlayServFindMatchRequest
                {
                    FunctionSlug = "arena",
                    Parameters = new Dictionary<string, object>
                    {
                        ["map"] = "desert",
                        ["crossplay"] = true
                    }
                }, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"params\""));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"map\":\"desert\""));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"crossplay\":true"));
        }

        [Test]
        public void Find_match_without_server_wait_keeps_the_ten_second_request_deadline()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"status\":\"not_found\"}");
            var client = CreateClient(fake);

            client.FindMatchAsync("arena", null, 0, 0, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(fake.Requests[0].TimeoutSeconds, Is.EqualTo(10));
        }

        [Test]
        public void Caller_cancellation_stops_join_before_network_io()
        {
            var fake = new FakeRuntimeHttpClient();
            var client = CreateClient(fake);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                client.JoinGameAsync("arena", null, null, cancellation.Token)
                    .GetAwaiter()
                    .GetResult());

            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Searching_response_is_retried_inside_one_join_call()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"status\":\"searching\",\"retry_after_ms\":1}");
            fake.Enqueue(
                "{\"status\":\"matched\",\"room_name\":\"arena-2\",\"reservation_token\":\"rsv_2\",\"expires_at\":\"2026-08-19T00:00:00Z\"}");
            var client = CreateClient(fake, (_, __) => Task.CompletedTask);

            var result = client.JoinGameAsync("arena", null, null, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServJoinGameStatus.Matched));
            Assert.That(result.Reservation.RoomName, Is.EqualTo("arena-2"));
            Assert.That(fake.Requests, Has.Count.EqualTo(2));
            Assert.That(fake.Requests[0].TimeoutSeconds, Is.EqualTo(25));
            Assert.That(fake.Requests[1].TimeoutSeconds, Is.EqualTo(25));
        }

        [Test]
        public void Join_reuses_parameter_snapshot_and_tracks_search_age_across_polls()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"status\":\"searching\",\"retry_after_ms\":250}");
            fake.Enqueue(
                "{\"status\":\"matched\",\"room_name\":\"arena-2\",\"reservation_token\":\"rsv_2\",\"expires_at\":\"2026-08-19T00:00:00Z\"}");
            var now = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
            var parameters = new Dictionary<string, object> { ["mode"] = "duo" };
            var client = CreateClient(
                fake,
                (milliseconds, _) =>
                {
                    now = now.AddMilliseconds(milliseconds);
                    parameters["mode"] = "mutated-after-start";
                    return Task.CompletedTask;
                },
                () => now);

            var result = client.JoinGameAsync(new PlayServJoinGameRequest
                {
                    FunctionSlug = "arena",
                    Matchmaker = "ranked",
                    Parameters = parameters,
                    WaitMs = 1_234
                }, null, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServJoinGameStatus.Matched));
            Assert.That(fake.Requests, Has.Count.EqualTo(2));
            Assert.That(fake.Requests[0].TimeoutSeconds, Is.EqualTo(7));
            Assert.That(fake.Requests[1].TimeoutSeconds, Is.EqualTo(7));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"wait_ms\":1234"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"search_age_ms\":0"));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"search_age_ms\":250"));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"mode\":\"duo\""));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"mode\":\"duo\""));
            Assert.That(fake.Requests[1].JsonBody, Does.Not.Contain("mutated-after-start"));
        }

        [Test]
        public void Join_preserves_parameters_after_room_closed_reentry()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue(
                "{\"status\":\"matched\",\"room_name\":\"closed-room\",\"reservation_token\":\"rsv_1\",\"expires_at\":\"2026-08-19T00:00:00Z\"}");
            fake.Enqueue(
                "{\"status\":\"matched\",\"room_name\":\"open-room\",\"reservation_token\":\"rsv_2\",\"expires_at\":\"2026-08-19T00:00:00Z\"}");
            var client = CreateClient(fake);
            var entryAttempts = 0;

            var result = client.JoinGameAsync(new PlayServJoinGameRequest
                {
                    FunctionSlug = "arena",
                    Parameters = new { party = "party-7" }
                }, (_, __) =>
                {
                    entryAttempts++;
                    if (entryAttempts == 1)
                        throw new PlayServRoomEntryRefusedException("room_closed");
                    return Task.CompletedTask;
                }, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Reservation.RoomName, Is.EqualTo("open-room"));
            Assert.That(fake.Requests, Has.Count.EqualTo(2));
            Assert.That(fake.Requests[0].JsonBody, Does.Contain("\"party\":\"party-7\""));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"party\":\"party-7\""));
            Assert.That(fake.Requests[1].JsonBody, Does.Contain("\"search_age_ms\":0"));
        }

        [Test]
        public void Launch_server_posts_player_credentials_region_and_maps_accepted_deployment()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"deployment_id\":\"dep_123\",\"region\":\"eu-west\"}");
            var client = CreateClient(fake);

            var result = client.LaunchServerAsync("tank room", " eu-west ", default)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.DeploymentId, Is.EqualTo("dep_123"));
            Assert.That(result.Region, Is.EqualTo("eu-west"));
            var request = fake.Requests[0];
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.RelativePath, Is.EqualTo("rooms/tank%20room/servers:launch"));
            Assert.That(request.ClientToken, Is.EqualTo("pk_public"));
            Assert.That(request.BearerToken, Is.EqualTo("player.jwt.value"));
            Assert.That(request.JsonBody, Does.Contain("\"region\":\"eu-west\""));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(10));
        }

        [Test]
        public void Launch_server_omits_empty_region()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue("{\"deployment_id\":\"dep_123\",\"region\":null}");
            var client = CreateClient(fake);

            client.LaunchServerAsync("tank-room", null, default)
                .GetAwaiter()
                .GetResult();

            Assert.That(fake.Requests[0].JsonBody, Does.Not.Contain("region"));
        }

        [Test]
        public void Launch_server_rejects_empty_incomplete_and_malformed_success_responses()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.Enqueue(string.Empty);
            fake.Enqueue("{}");
            fake.Enqueue("not-json");
            var client = CreateClient(fake);

            Assert.That(
                Assert.Throws<PlayServMatchmakingException>(() =>
                    client.LaunchServerAsync("arena", null, default).GetAwaiter().GetResult())
                    .UnifiedError.SourceCode,
                Is.EqualTo("matchmaking_empty_response"));
            Assert.That(
                Assert.Throws<PlayServMatchmakingException>(() =>
                    client.LaunchServerAsync("arena", null, default).GetAwaiter().GetResult())
                    .UnifiedError.SourceCode,
                Is.EqualTo("matchmaking_incomplete_response"));
            Assert.That(
                Assert.Throws<PlayServMatchmakingException>(() =>
                    client.LaunchServerAsync("arena", null, default).GetAwaiter().GetResult())
                    .UnifiedError.SourceCode,
                Is.EqualTo("matchmaking_invalid_response"));
        }

        [Test]
        public void HttpProblem_IsNormalizedWithOperationSlugAndRetryability()
        {
            var fake = new FakeRuntimeHttpClient();
            fake.EnqueueException(new PlayServRuntimeHttpException(
                "rate limited",
                429,
                "{\"code\":\"matchmaking_busy\",\"retryable\":true}",
                "matchmaking_busy",
                false,
                problemDetail: "Try again shortly."));
            var client = CreateClient(fake);

            var exception = Assert.Throws<PlayServMatchmakingException>(() =>
                client.FindMatchAsync("arena", null, 0, 0, default).GetAwaiter().GetResult());

            Assert.That(exception.Operation, Is.EqualTo(PlayServMatchmakingOperation.FindMatch));
            Assert.That(exception.FunctionSlug, Is.EqualTo("arena"));
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.RateLimited));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("matchmaking_busy"));
            Assert.That(exception.UnifiedError.HttpStatus, Is.EqualTo(429));
            Assert.That(exception.UnifiedError.Retryable, Is.True);
        }

        [Test]
        public void InternalDeadlineAndUnsupportedStatus_AreTypedFailures()
        {
            var timeoutHttp = new FakeRuntimeHttpClient();
            timeoutHttp.Enqueue("{\"status\":\"not_found\"}", simulatedDurationSeconds: 11);
            var timeoutClient = CreateClient(timeoutHttp);

            var timeout = Assert.Throws<PlayServMatchmakingException>(() =>
                timeoutClient.FindMatchAsync("arena", null, 0, 0, default).GetAwaiter().GetResult());
            Assert.That(timeout.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(timeout.UnifiedError.Retryable, Is.True);

            var invalidHttp = new FakeRuntimeHttpClient();
            invalidHttp.Enqueue("{\"status\":\"queued_forever\"}");
            var invalidClient = CreateClient(invalidHttp);
            var unsupported = Assert.Throws<PlayServMatchmakingException>(() =>
                invalidClient.JoinGameAsync("arena", null, null, default).GetAwaiter().GetResult());
            Assert.That(unsupported.Operation, Is.EqualTo(PlayServMatchmakingOperation.JoinGame));
            Assert.That(unsupported.UnifiedError.SourceCode, Is.EqualTo("matchmaking_unsupported_status"));
        }

        [Test]
        public void RoomEntryRefusal_PreservesLegacyCodeAndAddsUnifiedError()
        {
            var exception = new PlayServRoomEntryRefusedException("room_closed");

            Assert.That(exception.ErrorCode, Is.EqualTo("room_closed"));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("room_closed"));
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Conflict));
        }

        [Test]
        public void Non_object_parameters_are_rejected_before_network_io()
        {
            var fake = new FakeRuntimeHttpClient();
            var client = CreateClient(fake);

            Assert.Throws<ArgumentException>(() =>
                client.FindMatchAsync(new PlayServFindMatchRequest
                    {
                        FunctionSlug = "arena",
                        Parameters = new[] { "duo", "ranked" }
                    }, default)
                    .GetAwaiter()
                    .GetResult());
            Assert.Throws<ArgumentException>(() =>
                client.JoinGameAsync(new PlayServJoinGameRequest
                    {
                        FunctionSlug = "arena",
                        Parameters = "not-an-object"
                    }, null, default)
                    .GetAwaiter()
                    .GetResult());
            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Full_api_validates_arguments_before_network_io()
        {
            var fake = new FakeRuntimeHttpClient();
            var client = CreateClient(fake);

            Assert.Throws<ArgumentNullException>(() =>
                client.FindMatchAsync((PlayServFindMatchRequest)null, default)
                    .GetAwaiter().GetResult());
            Assert.Throws<ArgumentNullException>(() =>
                client.JoinGameAsync((PlayServJoinGameRequest)null, null, default)
                    .GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() =>
                client.FindMatchAsync(new PlayServFindMatchRequest
                    { FunctionSlug = "bad/slug" }, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                client.FindMatchAsync(new PlayServFindMatchRequest
                    { FunctionSlug = "arena", WaitMs = 25_001 }, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                client.FindMatchAsync(new PlayServFindMatchRequest
                    { FunctionSlug = "arena", SearchAgeMs = -1 }, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                client.LaunchServerAsync("arena", new string('x', 65), default)
                    .GetAwaiter().GetResult());
            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Launch_cancellation_stops_before_network_io()
        {
            var fake = new FakeRuntimeHttpClient();
            var client = CreateClient(fake);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                client.LaunchServerAsync("arena", null, cancellation.Token)
                    .GetAwaiter().GetResult());
            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void Player_facade_does_not_expose_secret_server_lifecycle_operations()
        {
            var facade = typeof(PlayServMatchmaking);

            Assert.That(facade.GetMethod("ListRoomsAsync"), Is.Null);
            Assert.That(facade.GetMethod("UpsertRoomAsync"), Is.Null);
            Assert.That(facade.GetMethod("CloseRoomAsync"), Is.Null);
            Assert.That(facade.GetMethod("ConsumeReservationAsync"), Is.Null);
        }

        private static PlayServMatchmakingClient CreateClient(
            FakeRuntimeHttpClient fake,
            Func<int, CancellationToken, Task> delay = null,
            Func<DateTimeOffset> utcNow = null)
        {
            var settings = new PlayServSettings
            {
                ClientToken = "pk_public",
                RuntimeTokenProvider = new PlayServDelegateRuntimeTokenProvider(
                    _ => Task.FromResult("player.jwt.value"))
            };
            return new PlayServMatchmakingClient(
                settings,
                fake,
                new NewtonsoftJsonCodec(),
                utcNow: utcNow ?? (() => new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero)),
                delay: delay);
        }

        [Serializable]
        private sealed class LobbyParameters
        {
            public string mode;
            public int skill;
        }

        private sealed class FakeRuntimeHttpClient : IPlayServRuntimeHttpClient
        {
            private readonly Queue<FakeResponse> _responses = new Queue<FakeResponse>();

            public List<PlayServRuntimeDataRequest> Requests { get; } =
                new List<PlayServRuntimeDataRequest>();

            public void Enqueue(string body, int simulatedDurationSeconds = 0) =>
                _responses.Enqueue(new FakeResponse(body, simulatedDurationSeconds));

            public void EnqueueException(Exception exception) =>
                _responses.Enqueue(new FakeResponse(exception));

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Requests.Add(request);
                var response = _responses.Dequeue();
                if (response.Exception != null)
                    throw response.Exception;
                if (request.TimeoutSeconds.HasValue &&
                    response.SimulatedDurationSeconds > request.TimeoutSeconds.Value)
                {
                    throw new OperationCanceledException("Simulated request deadline elapsed.", ct);
                }

                return Task.FromResult(new PlayServRuntimeDataResponse(
                    200,
                    response.Body,
                    null,
                    null));
            }

            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> SignInAnonAsync(
                string clientToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerRefreshResponseDto> RefreshAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> LoginExternalAsync(
                string clientToken,
                PlayerExternalLoginRequestDto request,
                string playerAccessToken = null,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task SignOutAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            private sealed class FakeResponse
            {
                public FakeResponse(string body, int simulatedDurationSeconds)
                {
                    Body = body;
                    SimulatedDurationSeconds = simulatedDurationSeconds;
                }

                public FakeResponse(Exception exception)
                {
                    Exception = exception;
                }

                public string Body { get; }

                public int SimulatedDurationSeconds { get; }

                public Exception Exception { get; }
            }
        }
    }
}
