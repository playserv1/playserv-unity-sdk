using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Code;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServCodeTests
    {
        [Test]
        public void Typed_call_posts_json_with_player_auth_version_query_and_headers()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"value\":42}",
                null,
                null,
                "application/json; charset=utf-8",
                new Dictionary<string, string> { ["X-Trace"] = "trace-1" }));
            var client = CreateClient(http);

            var result = client.CallAsync<ValueResponse>(
                    "daily-reward",
                    new { amount = 7 },
                    new PlayServFunctionCallOptions
                    {
                        Version = "v2",
                        Query = new Dictionary<string, string>
                        {
                            ["region"] = "eu west",
                            ["mode"] = "ranked"
                        },
                        Headers = new Dictionary<string, string>
                        {
                            ["X-Correlation-Id"] = "corr-1"
                        },
                        TimeoutSeconds = 30
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.value, Is.EqualTo(42));
            Assert.That(result.Response.StatusCode, Is.EqualTo(200));
            Assert.That(result.Response.ContentType, Does.StartWith("application/json"));
            Assert.That(result.Response.Headers["X-Trace"], Is.EqualTo("trace-1"));

            var request = http.Requests[0];
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(
                request.RelativePath,
                Is.EqualTo("fn/daily-reward?mode=ranked&region=eu%20west"));
            Assert.That(request.ClientToken, Is.EqualTo("pk_public"));
            Assert.That(request.BearerToken, Is.EqualTo("player.jwt.value"));
            Assert.That(request.FunctionVersion, Is.EqualTo("v2"));
            Assert.That(request.Headers["X-Correlation-Id"], Is.EqualTo("corr-1"));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(30));
            Assert.That(request.JsonBody, Does.Contain("\"amount\":7"));
        }

        [Test]
        public void Raw_invoke_supports_put_and_explicit_content_type()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(204, string.Empty, null, null));
            var client = CreateClient(http, withPlayerToken: false);

            var result = client.InvokeAsync(new PlayServFunctionRequest
                {
                    Slug = "profile",
                    Method = PlayServFunctionMethod.Put,
                    RawBody = "name=Alex",
                    ContentType = "application/x-www-form-urlencoded"
                }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Response.StatusCode, Is.EqualTo(204));
            Assert.That(http.Requests[0].Method, Is.EqualTo("PUT"));
            Assert.That(http.Requests[0].JsonBody, Is.EqualTo("name=Alex"));
            Assert.That(
                http.Requests[0].ContentType,
                Is.EqualTo("application/x-www-form-urlencoded"));
            Assert.That(http.Requests[0].BearerToken, Is.Null);
        }

        [Test]
        public void Problem_details_are_returned_as_unified_error()
        {
            var http = new FakeRuntimeHttpClient
            {
                Exception = new PlayServRuntimeHttpException(
                    "forbidden",
                    403,
                    "{\"code\":\"function_execute_forbidden\",\"detail\":\"Execute denied\"}",
                    "function_execute_forbidden",
                    false,
                    problemDetail: "Execute denied")
            };
            var client = CreateClient(http);

            var result = client.CallAsync<ValueResponse>(
                    "closed-function",
                    body: null,
                    options: null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.Forbidden));
            Assert.That(result.Error.SourceCode, Is.EqualTo("function_execute_forbidden"));
            Assert.That(result.Error.HttpStatus, Is.EqualTo(403));
            Assert.That(result.Error.Retryable, Is.False);
            Assert.That(result.Response.StatusCode, Is.EqualTo(403));
            Assert.That(result.Response.Body, Does.Contain("Execute denied"));
        }

        [Test]
        public void Invalid_typed_response_returns_deserialization_error_with_raw_response()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "not-json",
                null,
                null,
                "text/plain"));
            var client = CreateClient(http);

            var result = client.CallAsync<ValueResponse>(
                    "bad-response",
                    null,
                    null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.Deserialization));
            Assert.That(result.Error.SourceCode, Is.EqualTo("function_response_deserialization_failed"));
            Assert.That(result.Response.Body, Is.EqualTo("not-json"));
        }

        [Test]
        public void String_response_returns_plain_text_without_json_parsing()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "pong",
                null,
                null,
                "text/plain"));
            var client = CreateClient(http);

            var result = client.CallAsync<string>(
                    "ping",
                    null,
                    null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo("pong"));
            Assert.That(result.Response.BodyBytes, Is.EqualTo(Encoding.UTF8.GetBytes("pong")));
        }

        [Test]
        public void Binary_invoke_preserves_exact_bytes_and_reports_progress()
        {
            var http = new FakeRuntimeHttpClient();
            var responseBytes = new byte[] { 0, 1, 127, 128, 255 };
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                string.Empty,
                null,
                null,
                "application/octet-stream",
                bodyBytes: responseBytes));
            var progress = new ImmediateProgress<PlayServFunctionTransferProgress>();
            var client = CreateClient(http);
            var requestBytes = new byte[] { 9, 8, 7, 0 };

            var result = client.InvokeBytesAsync(
                    new PlayServFunctionRequest
                    {
                        Slug = "asset-generator",
                        Method = PlayServFunctionMethod.Patch,
                        RawBodyBytes = requestBytes,
                        ContentType = "application/octet-stream"
                    },
                    new PlayServFunctionTransferOptions
                    {
                        MaxResponseBytes = 12345,
                        Progress = progress
                    },
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Response.BodyBytes, Is.EqualTo(responseBytes));
            Assert.That(http.BinaryRequests.Count, Is.EqualTo(1));
            Assert.That(http.BinaryRequests[0].BodyBytes, Is.EqualTo(requestBytes));
            Assert.That(http.BinaryRequests[0].MaxResponseBytes, Is.EqualTo(12345));
            Assert.That(http.BinaryRequests[0].Request.Method, Is.EqualTo("PATCH"));
            Assert.That(http.BinaryRequests[0].Request.BearerToken, Is.EqualTo("player.jwt.value"));
            Assert.That(progress.Values.Select(value => value.Direction), Does.Contain(PlayServFunctionTransferDirection.Upload));
            Assert.That(progress.Values.Select(value => value.Direction), Does.Contain(PlayServFunctionTransferDirection.Download));
        }

        [Test]
        public void Binary_body_is_exclusive_and_unsupported_custom_transport_fails_before_io()
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);
            Assert.Throws<ArgumentException>(() => client.InvokeBytesAsync(
                    new PlayServFunctionRequest
                    {
                        Slug = "bad",
                        RawBody = "text",
                        RawBodyBytes = new byte[] { 1 }
                    },
                    null,
                    CancellationToken.None)
                .GetAwaiter().GetResult());
            Assert.That(http.BinaryRequests, Is.Empty);

            var textOnly = new TextOnlyRuntimeHttpClient();
            var unsupported = CreateClient(textOnly).InvokeBytesAsync(
                    new PlayServFunctionRequest { Slug = "binary" },
                    null,
                    CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(unsupported.IsSuccess, Is.False);
            Assert.That(unsupported.Error.Code, Is.EqualTo(PlayServErrorCode.InvalidConfiguration));
            Assert.That(unsupported.Error.SourceCode, Is.EqualTo("binary_http_not_supported"));
            Assert.That(textOnly.Requests, Is.Empty);
        }

        [Test]
        public void Response_size_limit_has_typed_invalid_response_error()
        {
            var http = new FakeRuntimeHttpClient
            {
                BinaryException = new PlayServRuntimeHttpException(
                    "too large",
                    200,
                    string.Empty,
                    "function_response_too_large",
                    false)
            };

            var result = CreateClient(http).InvokeBytesAsync(
                    new PlayServFunctionRequest { Slug = "archive" },
                    null,
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
            Assert.That(result.Error.SourceCode, Is.EqualTo("function_response_too_large"));
            Assert.That(http.BinaryRequests[0].MaxResponseBytes,
                Is.EqualTo(PlayServFunctionTransferOptions.DefaultMaxResponseBytes));

            Assert.Throws<ArgumentOutOfRangeException>(() => CreateClient(http).InvokeBytesAsync(
                    new PlayServFunctionRequest { Slug = "archive" },
                    new PlayServFunctionTransferOptions { MaxResponseBytes = 0 },
                    CancellationToken.None)
                .GetAwaiter().GetResult());
            Assert.That(http.BinaryRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Binary_http_error_preserves_response_bytes_without_copying_payload_to_error_details()
        {
            var responseBytes = new byte[] { 0, 255, 17, 42 };
            var http = new FakeRuntimeHttpClient
            {
                BinaryException = new PlayServRuntimeHttpException(
                    "upstream failed",
                    502,
                    "opaque-binary-response",
                    "function_upstream_failed",
                    false,
                    responseBytes: responseBytes)
            };

            var result = CreateClient(http).InvokeBytesAsync(
                    new PlayServFunctionRequest { Slug = "archive" },
                    null,
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Response.BodyBytes, Is.EqualTo(responseBytes));
            Assert.That(result.Error.RawDetails, Is.Empty);
        }

        [Test]
        public void Download_streams_to_temp_then_commits_and_requires_explicit_overwrite()
        {
            var directory = Path.Combine(Path.GetTempPath(), "playserv-code-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "asset.bin");
            try
            {
                var http = new FakeRuntimeHttpClient();
                var firstBytes = new byte[] { 1, 2, 3, 4 };
                http.Enqueue(new PlayServRuntimeDataResponse(
                    200, string.Empty, null, null, "application/octet-stream", bodyBytes: firstBytes));
                var client = CreateClient(http);

                var first = client.DownloadToFileAsync(
                        new PlayServFunctionRequest { Slug = "asset" },
                        target,
                        null,
                        CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.That(first.IsSuccess, Is.True);
                Assert.That(first.FilePath, Is.EqualTo(Path.GetFullPath(target)));
                Assert.That(first.BytesWritten, Is.EqualTo(firstBytes.Length));
                Assert.That(File.ReadAllBytes(target), Is.EqualTo(firstBytes));
                Assert.That(http.BinaryRequests[0].MaxResponseBytes,
                    Is.EqualTo(PlayServFunctionDownloadOptions.DefaultMaxResponseBytes));
                Assert.That(http.BinaryRequests[0].DownloadFilePath, Does.Contain(".playserv-"));

                Assert.Throws<IOException>(() => client.DownloadToFileAsync(
                        new PlayServFunctionRequest { Slug = "asset" },
                        target,
                        null,
                        CancellationToken.None)
                    .GetAwaiter().GetResult());
                Assert.That(http.BinaryRequests.Count, Is.EqualTo(1));

                var replacement = new byte[] { 8, 9 };
                http.Enqueue(new PlayServRuntimeDataResponse(
                    200, string.Empty, null, null, "application/octet-stream", bodyBytes: replacement));
                var replaced = client.DownloadToFileAsync(
                        new PlayServFunctionRequest { Slug = "asset" },
                        target,
                        new PlayServFunctionDownloadOptions { OverwriteExistingFile = true },
                        CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.That(replaced.IsSuccess, Is.True);
                Assert.That(File.ReadAllBytes(target), Is.EqualTo(replacement));
                Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Failed_file_download_removes_partial_and_preserves_existing_target()
        {
            var directory = Path.Combine(Path.GetTempPath(), "playserv-code-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "asset.bin");
            var original = new byte[] { 4, 5, 6 };
            File.WriteAllBytes(target, original);
            try
            {
                var http = new FakeRuntimeHttpClient
                {
                    BinaryHandler = request =>
                    {
                        File.WriteAllBytes(request.DownloadFilePath, new byte[] { 99 });
                        throw new PlayServRuntimeHttpException(
                            "failed",
                            500,
                            "upstream failed",
                            "http_500",
                            false);
                    }
                };

                var result = CreateClient(http).DownloadToFileAsync(
                        new PlayServFunctionRequest { Slug = "asset" },
                        target,
                        new PlayServFunctionDownloadOptions { OverwriteExistingFile = true },
                        CancellationToken.None)
                    .GetAwaiter().GetResult();

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.ServerError));
                Assert.That(File.ReadAllBytes(target), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Reserved_headers_are_rejected_before_network_io()
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);

            Assert.Throws<ArgumentException>(() =>
                client.InvokeAsync(new PlayServFunctionRequest
                    {
                        Slug = "echo",
                        Headers = new Dictionary<string, string>
                        {
                            ["Authorization"] = "Bearer secret"
                        }
                    }, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void Caller_cancellation_is_not_converted_to_a_result()
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                client.InvokeAsync(
                        new PlayServFunctionRequest { Slug = "echo" },
                        cancellation.Token)
                    .GetAwaiter()
                    .GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        private static PlayServCodeClient CreateClient(
            IPlayServRuntimeHttpClient http,
            bool withPlayerToken = true,
            IJsonCodec codec = null)
        {
            var settings = new PlayServSettings
            {
                BackendServerAddress = "https://platform.example",
                ClientToken = "pk_public",
                RuntimeTokenProvider = withPlayerToken
                    ? new PlayServDelegateRuntimeTokenProvider(
                        _ => Task.FromResult("player.jwt.value"))
                    : null
            };
            return new PlayServCodeClient(settings, http, codec ?? new NewtonsoftJsonCodec());
        }

        [Serializable]
        private sealed class ValueResponse
        {
            public int value;
        }

        private sealed class FakeRuntimeHttpClient :
            IPlayServRuntimeHttpClient,
            IPlayServRuntimeBinaryHttpClient
        {
            private readonly Queue<PlayServRuntimeDataResponse> _responses =
                new Queue<PlayServRuntimeDataResponse>();

            public readonly List<PlayServRuntimeDataRequest> Requests =
                new List<PlayServRuntimeDataRequest>();

            public readonly List<PlayServRuntimeBinaryDataRequest> BinaryRequests =
                new List<PlayServRuntimeBinaryDataRequest>();

            public PlayServRuntimeHttpException Exception { get; set; }
            public Func<PlayServRuntimeDataRequest, Task<PlayServRuntimeDataResponse>> Handler { get; set; }

            public PlayServRuntimeHttpException BinaryException { get; set; }

            public Func<PlayServRuntimeBinaryDataRequest, PlayServRuntimeDataResponse> BinaryHandler { get; set; }

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
                if (Handler != null) return Handler(request);
                return Task.FromResult(_responses.Dequeue());
            }

            public Task<PlayServRuntimeDataResponse> SendBinaryDataAsync(
                PlayServRuntimeBinaryDataRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                BinaryRequests.Add(request);
                if (BinaryException != null)
                    throw BinaryException;
                if (BinaryHandler != null)
                    return Task.FromResult(BinaryHandler(request));

                var response = _responses.Dequeue();
                request.Progress?.Report(new PlayServRuntimeTransferProgress(
                    PlayServRuntimeTransferDirection.Upload,
                    request.BodyBytes?.LongLength ?? 0,
                    request.BodyBytes?.LongLength));
                request.Progress?.Report(new PlayServRuntimeTransferProgress(
                    PlayServRuntimeTransferDirection.Download,
                    response.BodyBytes.LongLength,
                    response.BodyBytes.LongLength));
                if (string.IsNullOrWhiteSpace(request.DownloadFilePath))
                    return Task.FromResult(response);

                File.WriteAllBytes(request.DownloadFilePath, response.BodyBytes);
                return Task.FromResult(new PlayServRuntimeDataResponse(
                    response.StatusCode,
                    string.Empty,
                    response.ETag,
                    response.Location,
                    response.ContentType,
                    response.Headers,
                    bodyBytes: null,
                    bytesWritten: response.BodyBytes.LongLength));
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

        private sealed class TextOnlyRuntimeHttpClient : IPlayServRuntimeHttpClient
        {
            public List<PlayServRuntimeDataRequest> Requests { get; } =
                new List<PlayServRuntimeDataRequest>();

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                Requests.Add(request);
                throw new NotSupportedException();
            }

            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> SignInAnonAsync(string clientToken, CancellationToken ct = default) =>
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

        private sealed class ImmediateProgress<T> : IProgress<T>
        {
            public List<T> Values { get; } = new List<T>();

            public void Report(T value) => Values.Add(value);
        }
    }
}
