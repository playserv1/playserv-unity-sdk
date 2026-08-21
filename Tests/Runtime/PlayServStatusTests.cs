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
