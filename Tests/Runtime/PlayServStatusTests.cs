using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Status;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServStatusTests
    {
        [TestCase(null, "status/my%20project")]
        [TestCase(" qa/eu ", "status/my%20project?env=qa%2Feu")]
        public void Project_status_is_credential_free_and_preserves_optional_evidence(string environment, string path)
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(200,
                "{\"project_slug\":\"my project\",\"generated_at\":\"2026-09-06T10:00:00Z\",\"functions\":[" +
                "{\"env\":\"qa/eu\",\"slug\":\"score\",\"function_id\":\"fn_1\",\"kind\":\"function\",\"enabled\":true," +
                "\"deploy_status\":\"ready\",\"revision\":\"v7\",\"revision_ready\":null,\"deployed_at\":null," +
                "\"counters\":{\"since\":\"2026-09-06T00:00:00Z\",\"series_id\":\"series-1\",\"invocations\":9,\"failures\":2,\"timeouts\":1,\"upstream_5xx\":3,\"last_success_at\":null,\"last_failure_at\":\"2026-09-06T09:00:00Z\",\"last_duration_ms\":null,\"last_overhead_ms\":2}," +
                "\"probe\":{\"last_at\":\"2026-09-06T09:00:00Z\",\"ok\":false}," +
                "\"latency\":{\"p50_ms\":null,\"p95_ms\":12.5,\"p99_ms\":null,\"series\":[{\"at\":\"2026-09-06T09:00:00Z\",\"p95_ms\":null}]}}," +
                "{\"env\":\"prod\",\"slug\":\"idle\",\"function_id\":\"fn_2\",\"kind\":\"service\",\"enabled\":false,\"counters\":null,\"probe\":null,\"latency\":null}]}", null, null));
            var result = CreateClient(http).GetProjectAsync(" my project ", environment, default).GetAwaiter().GetResult();
            Assert.That(result.ProjectSlug, Is.EqualTo("my project"));
            Assert.That(result.GeneratedAt, Is.EqualTo(DateTimeOffset.Parse("2026-09-06T10:00:00Z")));
            var function = result.Functions[0];
            Assert.That(function.Environment, Is.EqualTo("qa/eu"));
            Assert.That(function.FunctionId, Is.EqualTo("fn_1"));
            Assert.That(function.DeployStatus, Is.EqualTo("ready"));
            Assert.That(function.Revision, Is.EqualTo("v7"));
            Assert.That(function.RevisionReady, Is.Null);
            Assert.That(function.DeployedAt, Is.Null);
            Assert.That(function.Counters.Invocations, Is.EqualTo(9));
            Assert.That(function.Counters.Upstream5xx, Is.EqualTo(3));
            Assert.That(function.Counters.LastDurationMs, Is.Null);
            Assert.That(function.Probe.Ok, Is.False);
            Assert.That(function.Latency.P95Ms, Is.EqualTo(12.5));
            Assert.That(function.Latency.P50Ms, Is.Null);
            Assert.That(function.Latency.Series[0].P95Ms, Is.Null);
            Assert.That(result.Functions[1].Counters, Is.Null);
            Assert.That(result.Functions[1].Probe, Is.Null);
            Assert.That(result.Functions[1].Latency, Is.Null);
            Assert.That(http.Requests[0].RelativePath, Is.EqualTo(path));
            Assert.That(http.Requests[0].RequiresClientToken, Is.False);
            Assert.That(http.Requests[0].ClientToken, Is.Null.Or.Empty);
            Assert.That(http.Requests[0].BearerToken, Is.Null.Or.Empty);
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase("{broken")]
        [TestCase("{\"project_slug\":\"p\",\"generated_at\":\"2026-09-06T10:00:00Z\",\"functions\":[null]}")]
        public void Project_status_rejects_missing_or_malformed_success(string body)
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(200, body, null, null));
            var exception = Assert.Throws<PlayServStatusException>(() => CreateClient(http).GetProjectAsync("project", null, default).GetAwaiter().GetResult());
            Assert.That(exception.UnifiedError.IsError, Is.True);
        }

        [Test]
        public void Project_status_validates_before_io_and_preserves_not_found_error()
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);
            Assert.Throws<ArgumentException>(() => client.GetProjectAsync(" ", null, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.GetProjectAsync("..", null, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.GetProjectAsync("project", "bad\nvalue", default).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => client.GetProjectAsync("project", null, new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
            http.Exception = new PlayServRuntimeHttpException("Not found", 404, "{}", "project_not_found", false);
            var error = Assert.Throws<PlayServStatusException>(() => client.GetProjectAsync("project", null, default).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.NotFound));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("project_not_found"));
        }

        [Test]
        public void Runtime_requests_require_client_token_by_default()
        {
            Assert.That(new PlayServRuntimeDataRequest().RequiresClientToken, Is.True);
        }

        [Test]
        public void Current_status_is_typed_and_credential_exempt()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"generated_at\":\"2026-08-19T10:00:00Z\",\"overall\":\"yellow\",\"items\":[{\"pop\":\"iad-1\",\"system\":\"data\",\"current_status\":\"yellow\",\"last_status\":\"green\",\"last_signal\":\"loaded\",\"last_probe_at\":\"2026-08-19T09:59:00Z\",\"seconds_since_probe\":60}]}",
                null,
                null));
            var client = CreateClient(http);

            var status = client.GetCurrentAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(status.Overall, Is.EqualTo(PlayServPlatformHealthStatuses.Yellow));
            Assert.That(status.Items.Length, Is.EqualTo(1));
            Assert.That(status.Items[0].Pop, Is.EqualTo("iad-1"));
            Assert.That(status.Items[0].CurrentStatus, Is.EqualTo("yellow"));
            Assert.That(status.Items[0].SecondsSinceProbe, Is.EqualTo(60));
            Assert.That(http.Requests[0].RelativePath, Is.EqualTo("status"));
            Assert.That(http.Requests[0].RequiresClientToken, Is.False);
            Assert.That(http.Requests[0].ClientToken, Is.Null.Or.Empty);
            Assert.That(http.Requests[0].BearerToken, Is.Null.Or.Empty);
        }

        [Test]
        public void History_encodes_pop_and_maps_daily_rollups()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"pop\":\"eu west\",\"days_requested\":7,\"systems\":[{\"system\":\"transport\",\"days\":[{\"day\":\"2026-08-18\",\"availability_pct\":99.75,\"green_minutes\":1430,\"yellow_minutes\":5,\"red_minutes\":5}]}]}",
                null,
                null));
            var client = CreateClient(http);

            var history = client.GetHistoryAsync(" eu west ", 7, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(http.Requests[0].RelativePath, Is.EqualTo("status/history?days=7&pop=eu%20west"));
            Assert.That(history.DaysRequested, Is.EqualTo(7));
            Assert.That(history.Systems[0].Days[0].AvailabilityPercent, Is.EqualTo(99.75m));
            Assert.That(history.Systems[0].Days[0].Day, Is.EqualTo("2026-08-18"));
        }

        [TestCase(0)]
        [TestCase(366)]
        public void Invalid_history_window_is_rejected_before_io(int days)
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                client.GetHistoryAsync(null, days, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void Federation_validates_and_deduplicates_origins_without_fetching_them()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"origins\":[\"https://platform.eu.playserv.com/\",\"https://PLATFORM.eu.playserv.com\",\"http://localhost:8080\"]}",
                null,
                null));
            var client = CreateClient(http);

            var federation = client.GetFederationAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(federation.Origins.Count, Is.EqualTo(2));
            Assert.That(federation.Origins[0].Scheme, Is.EqualTo("https"));
            Assert.That(federation.Origins[1].Port, Is.EqualTo(8080));
            Assert.That(http.Requests.Count, Is.EqualTo(1));
            Assert.That(http.Requests[0].RelativePath, Is.EqualTo("status/federation"));
        }

        [Test]
        public void Invalid_federation_origin_is_a_typed_invalid_response()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"origins\":[\"javascript:alert(1)\"]}",
                null,
                null));
            var client = CreateClient(http);

            var exception = Assert.Throws<PlayServStatusException>(() =>
                client.GetFederationAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("status_federation_origin_invalid"));
        }

        [Test]
        public void Problem_details_are_mapped_to_unified_exception()
        {
            var http = new FakeRuntimeHttpClient
            {
                Exception = new PlayServRuntimeHttpException(
                    "unavailable",
                    503,
                    "{\"code\":\"status_unavailable\",\"detail\":\"Try later\"}",
                    "status_unavailable",
                    false,
                    problemDetail: "Try later")
            };
            var client = CreateClient(http);

            var exception = Assert.Throws<PlayServStatusException>(() =>
                client.GetCurrentAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.ServerError));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("status_unavailable"));
            Assert.That(exception.UnifiedError.HttpStatus, Is.EqualTo(503));
            Assert.That(exception.UnifiedError.Retryable, Is.True);
        }

        [Test]
        public void Cancellation_is_not_wrapped()
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                client.GetCurrentAsync(cancellation.Token)
                    .GetAwaiter()
                    .GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        private static PlayServStatusClient CreateClient(FakeRuntimeHttpClient http) =>
            new PlayServStatusClient(
                new PlayServSettings { BackendServerAddress = "https://platform.example" },
                http,
                new NewtonsoftJsonCodec());

        private sealed class FakeRuntimeHttpClient : IPlayServRuntimeHttpClient
        {
            private readonly Queue<PlayServRuntimeDataResponse> _responses =
                new Queue<PlayServRuntimeDataResponse>();

            public readonly List<PlayServRuntimeDataRequest> Requests =
                new List<PlayServRuntimeDataRequest>();

            public PlayServRuntimeHttpException Exception { get; set; }

            public void Enqueue(PlayServRuntimeDataResponse response) =>
                _responses.Enqueue(response);

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Requests.Add(request);
                if (Exception != null)
                    throw Exception;
                return Task.FromResult(_responses.Dequeue());
            }

            public Task<string> GetLatestVersionAsync(
                string gameId,
                CancellationToken ct = default) =>
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
        }
    }
}
