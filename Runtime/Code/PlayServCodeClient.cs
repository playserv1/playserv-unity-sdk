using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Code
{
    internal sealed class PlayServCodeClient
    {
        private readonly PlayServSettings _settings;
        private readonly IPlayServRuntimeHttpClient _http;
        private readonly IJsonCodec _json;
        private readonly bool _requiresClientToken;

        internal PlayServCodeClient(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient http,
            IJsonCodec json,
            bool requiresClientToken = true)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _requiresClientToken = requiresClientToken;
        }

        internal static PlayServCodeClient CreateDefault()
        {
            var settings = PlayServ.Settings;
            var json = new NewtonsoftJsonCodec();
            var http = PlayServRuntimeHttpClientResolver.Create(
                new PlayServHttpModuleContext(settings.ToRuntimeSettings(), json));
            return new PlayServCodeClient(settings, http, json);
        }

        internal async Task<PlayServFunctionResult> InvokeAsync(
            PlayServFunctionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request);
            if (request.RawBodyBytes != null)
            {
                return await InvokeBytesAsync(
                    request,
                    new PlayServFunctionTransferOptions(),
                    cancellationToken);
            }
            EnsureConfigured();

            string body;
            try
            {
                body = request.RawBody ?? (request.Body == null ? null : _json.Serialize(request.Body));
            }
            catch (Exception exception)
            {
                return Failure(
                    response: null,
                    new PlayServError(
                        PlayServErrorCode.Serialization,
                        "function_request_serialization_failed",
                        "Cloud-function request serialization failed.",
                        rawDetails: exception.Message));
            }

            string bearer;
            try
            {
                bearer = await ResolvePlayerTokenAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failure(
                    response: null,
                    new PlayServError(
                        PlayServErrorCode.Unauthorized,
                        "runtime_token_provider_failed",
                        "The runtime player token provider failed.",
                        rawDetails: exception.Message));
            }

            try
            {
                var response = await _http.SendDataAsync(new PlayServRuntimeDataRequest
                {
                    Method = MethodToWire(request.Method),
                    RelativePath = BuildPath(request.Slug, request.Query),
                    ClientToken = _settings.ClientToken,
                    BearerToken = bearer,
                    JsonBody = body,
                    ContentType = request.ContentType,
                    FunctionVersion = NormalizeOptional(request.Version),
                    Headers = request.Headers,
                    TimeoutSeconds = request.TimeoutSeconds
                }, cancellationToken);

                if (response == null)
                {
                    return Failure(
                        response: null,
                        new PlayServError(
                            PlayServErrorCode.InvalidResponse,
                            "function_response_missing",
                            "Cloud-function response is missing."));
                }

                return new PlayServFunctionResult(ToResponse(response), PlayServError.None);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServRuntimeHttpException exception)
            {
                return Failure(ToResponse(exception), ToError(exception));
            }
            catch (Exception exception)
            {
                return Failure(
                    response: null,
                    new PlayServError(
                        PlayServErrorCode.Network,
                        "function_transport_failed",
                        "Cloud-function request failed before a response was received.",
                        retryable: true,
                        rawDetails: exception.Message));
            }
        }

        internal async Task<PlayServFunctionResult> InvokeBytesAsync(
            PlayServFunctionRequest request,
            PlayServFunctionTransferOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request);
            options = options ?? new PlayServFunctionTransferOptions();
            ValidateMaxResponseBytes(options.MaxResponseBytes, nameof(options));
            EnsureConfigured();

            if (!(_http is IPlayServRuntimeBinaryHttpClient binaryHttp))
            {
                return Failure(
                    null,
                    new PlayServError(
                        PlayServErrorCode.InvalidConfiguration,
                        "binary_http_not_supported",
                        "The configured PlayServ HTTP module does not support binary function transfers."));
            }

            byte[] bodyBytes;
            try
            {
                bodyBytes = BuildBodyBytes(request);
            }
            catch (Exception exception)
            {
                return Failure(
                    null,
                    new PlayServError(
                        PlayServErrorCode.Serialization,
                        "function_request_serialization_failed",
                        "Cloud-function request serialization failed.",
                        rawDetails: exception.Message));
            }

            var bearerResult = await TryResolvePlayerTokenAsync(cancellationToken);
            if (bearerResult.Error != null)
                return Failure(null, bearerResult.Error);

            try
            {
                var response = await binaryHttp.SendBinaryDataAsync(
                    new PlayServRuntimeBinaryDataRequest
                    {
                        Request = BuildRuntimeRequest(request, bearerResult.Token, null),
                        BodyBytes = bodyBytes,
                        MaxResponseBytes = options.MaxResponseBytes,
                        Progress = AdaptProgress(options.Progress)
                    },
                    cancellationToken);
                if (response == null)
                    return MissingResponseFailure();
                return new PlayServFunctionResult(ToResponse(response), PlayServError.None);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServRuntimeHttpException exception)
            {
                return Failure(ToResponse(exception), ToError(exception, includeRawResponse: false));
            }
            catch (Exception exception)
            {
                return Failure(null, TransportError(exception));
            }
        }

        internal async Task<PlayServFunctionDownloadResult> DownloadToFileAsync(
            PlayServFunctionRequest request,
            string filePath,
            PlayServFunctionDownloadOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException(
                "PlayServCode.DownloadToFileAsync is unavailable on WebGL. Use InvokeBytesAsync instead.");
#else
            ValidateRequest(request);
            options = options ?? new PlayServFunctionDownloadOptions();
            ValidateMaxResponseBytes(options.MaxResponseBytes, nameof(options));
            var targetPath = ValidateDownloadPath(filePath, options.OverwriteExistingFile);
            EnsureConfigured();

            if (!(_http is IPlayServRuntimeBinaryHttpClient binaryHttp))
            {
                return DownloadFailure(
                    targetPath,
                    null,
                    new PlayServError(
                        PlayServErrorCode.InvalidConfiguration,
                        "binary_http_not_supported",
                        "The configured PlayServ HTTP module does not support binary function transfers."));
            }

            byte[] bodyBytes;
            try
            {
                bodyBytes = BuildBodyBytes(request);
            }
            catch (Exception exception)
            {
                return DownloadFailure(
                    targetPath,
                    null,
                    new PlayServError(
                        PlayServErrorCode.Serialization,
                        "function_request_serialization_failed",
                        "Cloud-function request serialization failed.",
                        rawDetails: exception.Message));
            }

            var bearerResult = await TryResolvePlayerTokenAsync(cancellationToken);
            if (bearerResult.Error != null)
                return DownloadFailure(targetPath, null, bearerResult.Error);

            var temporaryPath = targetPath + ".playserv-" + Guid.NewGuid().ToString("N") + ".tmp";
            PlayServRuntimeDataResponse runtimeResponse = null;
            try
            {
                runtimeResponse = await binaryHttp.SendBinaryDataAsync(
                    new PlayServRuntimeBinaryDataRequest
                    {
                        Request = BuildRuntimeRequest(request, bearerResult.Token, null),
                        BodyBytes = bodyBytes,
                        MaxResponseBytes = options.MaxResponseBytes,
                        DownloadFilePath = temporaryPath,
                        Progress = AdaptProgress(options.Progress)
                    },
                    cancellationToken);
                if (runtimeResponse == null)
                {
                    TryDeleteFile(temporaryPath);
                    return DownloadFailure(
                        targetPath,
                        null,
                        new PlayServError(
                            PlayServErrorCode.InvalidResponse,
                            "function_response_missing",
                            "Cloud-function response is missing."));
                }

                CommitDownloadedFile(temporaryPath, targetPath, options.OverwriteExistingFile);
                return new PlayServFunctionDownloadResult(
                    targetPath,
                    runtimeResponse.BytesWritten,
                    ToResponse(runtimeResponse),
                    PlayServError.None);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(temporaryPath);
                throw;
            }
            catch (PlayServRuntimeHttpException exception)
            {
                TryDeleteFile(temporaryPath);
                return DownloadFailure(
                    targetPath,
                    ToResponse(exception),
                    ToError(exception, includeRawResponse: false));
            }
            catch (IOException exception)
            {
                TryDeleteFile(temporaryPath);
                return DownloadFailure(
                    targetPath,
                    runtimeResponse == null ? null : ToResponse(runtimeResponse),
                    new PlayServError(
                        PlayServErrorCode.Persistence,
                        "function_download_file_failed",
                        "The cloud-function response could not be committed to the target file.",
                        rawDetails: exception.Message));
            }
            catch (UnauthorizedAccessException exception)
            {
                TryDeleteFile(temporaryPath);
                return DownloadFailure(
                    targetPath,
                    runtimeResponse == null ? null : ToResponse(runtimeResponse),
                    new PlayServError(
                        PlayServErrorCode.Persistence,
                        "function_download_file_failed",
                        "The cloud-function response could not be committed to the target file.",
                        rawDetails: exception.Message));
            }
            catch (Exception exception)
            {
                TryDeleteFile(temporaryPath);
                return DownloadFailure(targetPath, null, TransportError(exception));
            }
#endif
        }

        internal async Task<PlayServFunctionResult<TResponse>> CallAsync<TResponse>(
            string slug,
            object body,
            PlayServFunctionCallOptions options,
            CancellationToken cancellationToken)
        {
            options = options ?? new PlayServFunctionCallOptions();
            var raw = await InvokeAsync(new PlayServFunctionRequest
            {
                Slug = slug,
                Method = PlayServFunctionMethod.Post,
                Body = body,
                Version = options.Version,
                Query = options.Query,
                Headers = options.Headers,
                TimeoutSeconds = options.TimeoutSeconds
            }, cancellationToken);

            if (!raw.IsSuccess)
            {
                return new PlayServFunctionResult<TResponse>(
                    default,
                    raw.Response,
                    raw.Error);
            }

            if (typeof(TResponse) == typeof(string))
            {
                return new PlayServFunctionResult<TResponse>(
                    (TResponse)(object)(raw.Response?.Body ?? string.Empty),
                    raw.Response,
                    PlayServError.None);
            }

            if (string.IsNullOrWhiteSpace(raw.Response?.Body))
            {
                return new PlayServFunctionResult<TResponse>(
                    default,
                    raw.Response,
                    PlayServError.None);
            }

            try
            {
                return new PlayServFunctionResult<TResponse>(
                    _json.Deserialize<TResponse>(raw.Response.Body),
                    raw.Response,
                    PlayServError.None);
            }
            catch (Exception exception)
            {
                return new PlayServFunctionResult<TResponse>(
                    default,
                    raw.Response,
                    new PlayServError(
                        PlayServErrorCode.Deserialization,
                        "function_response_deserialization_failed",
                        $"Cloud-function response could not be deserialized as {typeof(TResponse).Name}.",
                        rawDetails: exception.Message));
            }
        }

        private async Task<string> ResolvePlayerTokenAsync(CancellationToken cancellationToken)
        {
            if (_settings.RuntimeTokenProvider != null)
                return await _settings.RuntimeTokenProvider.GetTokenAsync(cancellationToken);

            return NormalizeOptional(_settings.PlayerAccessToken);
        }

        private async Task<TokenResolution> TryResolvePlayerTokenAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return new TokenResolution(
                    await ResolvePlayerTokenAsync(cancellationToken),
                    null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new TokenResolution(
                    null,
                    new PlayServError(
                        PlayServErrorCode.Unauthorized,
                        "runtime_token_provider_failed",
                        "The runtime player token provider failed.",
                        rawDetails: exception.Message));
            }
        }

        private void EnsureConfigured()
        {
            if (_requiresClientToken && string.IsNullOrWhiteSpace(_settings.ClientToken))
            {
                throw new InvalidOperationException(
                    "PlayServ.Settings.ClientToken must contain a public pk_* token before calling cloud functions.");
            }
            if (_requiresClientToken)
                PlayServCredentialPolicy.NormalizeClientToken(_settings.ClientToken);
            if (string.IsNullOrWhiteSpace(_settings.BackendServerAddress))
                throw new InvalidOperationException("PlayServ.Settings.BackendServerAddress is required before calling cloud functions.");
        }

        private static void ValidateRequest(PlayServFunctionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var slug = (request.Slug ?? string.Empty).Trim();
            if (slug.Length == 0)
                throw new ArgumentException("Cloud-function slug is required.", nameof(request));
            if (slug.IndexOf('/') >= 0 || slug.IndexOf('?') >= 0 || slug.IndexOf('#') >= 0)
                throw new ArgumentException("Cloud-function slug must not contain a path, query, or fragment.", nameof(request));
            var bodyKinds = (request.Body == null ? 0 : 1) +
                            (request.RawBody == null ? 0 : 1) +
                            (request.RawBodyBytes == null ? 0 : 1);
            if (bodyKinds > 1)
            {
                throw new ArgumentException(
                    "Cloud-function Body, RawBody, and RawBodyBytes are mutually exclusive.",
                    nameof(request));
            }
            if (!Enum.IsDefined(typeof(PlayServFunctionMethod), request.Method))
                throw new ArgumentOutOfRangeException(nameof(request), request.Method, "Unsupported cloud-function HTTP method.");
            if (request.TimeoutSeconds.HasValue && request.TimeoutSeconds.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(request), "Cloud-function timeout must be positive.");

            ValidateHeaderValue("Content-Type", request.ContentType, allowEmpty: true);
            ValidateHeaderValue("X-Playserv-Function-Version", request.Version, allowEmpty: true);
            ValidateHeaders(request.Headers);
            ValidateQuery(request.Query);
        }

        private static void ValidateHeaders(IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null)
                return;

            foreach (var header in headers)
            {
                var name = (header.Key ?? string.Empty).Trim();
                if (name.Length == 0)
                    throw new ArgumentException("Cloud-function header names cannot be empty.", nameof(headers));
                foreach (var character in name)
                {
                    if (!IsHeaderNameCharacter(character))
                        throw new ArgumentException($"Cloud-function header name '{name}' is invalid.", nameof(headers));
                }
                if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "X-Playserv-Client", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("X-Playserv-", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Cloud-function header '{name}' is reserved by PlayServ.", nameof(headers));
                }
                ValidateHeaderValue(name, header.Value, allowEmpty: false);
            }
        }

        private static void ValidateQuery(IReadOnlyDictionary<string, string> query)
        {
            if (query == null)
                return;
            foreach (var pair in query)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    throw new ArgumentException("Cloud-function query parameter names cannot be empty.", nameof(query));
            }
        }

        private static void ValidateHeaderValue(string name, string value, bool allowEmpty)
        {
            if (value == null)
                return;
            if (!allowEmpty && value.Length == 0)
                throw new ArgumentException($"Cloud-function header '{name}' cannot be empty.");
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException($"Cloud-function header '{name}' cannot contain line breaks.");
        }

        private static bool IsHeaderNameCharacter(char character) =>
            char.IsLetterOrDigit(character) ||
            character == '!' || character == '#' || character == '$' ||
            character == '%' || character == '&' || character == '\'' ||
            character == '*' || character == '+' || character == '-' ||
            character == '.' || character == '^' || character == '_' ||
            character == '`' || character == '|' || character == '~';

        private static string BuildPath(
            string slug,
            IReadOnlyDictionary<string, string> query)
        {
            var path = "fn/" + Uri.EscapeDataString(slug.Trim());
            if (query == null || query.Count == 0)
                return path;

            var encoded = query
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Concat(
                    Uri.EscapeDataString(pair.Key),
                    "=",
                    Uri.EscapeDataString(pair.Value ?? string.Empty)));
            return path + "?" + string.Join("&", encoded);
        }

        private static string MethodToWire(PlayServFunctionMethod method)
        {
            switch (method)
            {
                case PlayServFunctionMethod.Get:
                    return "GET";
                case PlayServFunctionMethod.Post:
                    return "POST";
                case PlayServFunctionMethod.Put:
                    return "PUT";
                case PlayServFunctionMethod.Patch:
                    return "PATCH";
                case PlayServFunctionMethod.Delete:
                    return "DELETE";
                default:
                    throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported cloud-function HTTP method.");
            }
        }

        private static PlayServFunctionResponse ToResponse(PlayServRuntimeDataResponse response)
        {
            if (response == null)
                return null;
            return new PlayServFunctionResponse(
                response.StatusCode,
                response.Body,
                response.ContentType,
                response.Headers,
                GetResponseBytes(response.Body, response.BodyBytes));
        }

        private static PlayServFunctionResponse ToResponse(PlayServRuntimeHttpException exception) =>
            new PlayServFunctionResponse(
                exception.StatusCode,
                exception.ResponseBody,
                string.Empty,
                null,
                GetResponseBytes(exception.ResponseBody, exception.ResponseBytes));

        private static byte[] GetResponseBytes(string body, byte[] exactBytes)
        {
            if (exactBytes != null && exactBytes.Length > 0)
                return exactBytes;
            return string.IsNullOrEmpty(body) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);
        }

        private static PlayServError ToError(
            PlayServRuntimeHttpException exception,
            bool includeRawResponse = true)
        {
            if (string.Equals(
                    exception.BackendCode,
                    "function_response_too_large",
                    StringComparison.Ordinal))
            {
                return new PlayServError(
                    PlayServErrorCode.InvalidResponse,
                    exception.BackendCode,
                    "Cloud-function response exceeded the configured response-size limit.",
                    exception.StatusCode > 0 ? (int?)exception.StatusCode : null);
            }

            return PlayServError.FromHttp(
                exception.StatusCode,
                exception.BackendCode,
                FirstNonEmpty(exception.ProblemDetail, exception.Message),
                exception.IsNetworkError,
                exception.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0,
                includeRawResponse ? exception.ResponseBody : null);
        }

        private static PlayServError TransportError(Exception exception) =>
            new PlayServError(
                PlayServErrorCode.Network,
                "function_transport_failed",
                "Cloud-function request failed before a response was received.",
                retryable: true,
                rawDetails: exception.Message);

        private PlayServRuntimeDataRequest BuildRuntimeRequest(
            PlayServFunctionRequest request,
            string bearer,
            string textBody) =>
            new PlayServRuntimeDataRequest
            {
                Method = MethodToWire(request.Method),
                RelativePath = BuildPath(request.Slug, request.Query),
                ClientToken = _settings.ClientToken,
                BearerToken = bearer,
                JsonBody = textBody,
                ContentType = request.ContentType,
                FunctionVersion = NormalizeOptional(request.Version),
                Headers = request.Headers,
                TimeoutSeconds = request.TimeoutSeconds
            };

        private byte[] BuildBodyBytes(PlayServFunctionRequest request)
        {
            if (request.RawBodyBytes != null)
                return (byte[])request.RawBodyBytes.Clone();
            if (request.RawBody != null)
                return Encoding.UTF8.GetBytes(request.RawBody);
            if (request.Body != null)
                return Encoding.UTF8.GetBytes(_json.Serialize(request.Body));
            return null;
        }

        private static IProgress<PlayServRuntimeTransferProgress> AdaptProgress(
            IProgress<PlayServFunctionTransferProgress> progress) =>
            progress == null ? null : new FunctionProgressAdapter(progress);

        private static void ValidateMaxResponseBytes(long maxResponseBytes, string argumentName)
        {
            if (maxResponseBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    argumentName,
                    "Cloud-function maximum response size must be positive.");
            }
        }

        private static string ValidateDownloadPath(string filePath, bool overwriteExisting)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A download target path is required.", nameof(filePath));
            if (!Path.IsPathRooted(filePath))
                throw new ArgumentException("The download target path must be absolute.", nameof(filePath));

            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("The download target must have a parent directory.", nameof(filePath));
            Directory.CreateDirectory(directory);
            if (!overwriteExisting && File.Exists(fullPath))
                throw new IOException($"Download target '{fullPath}' already exists.");
            return fullPath;
        }

        private static void CommitDownloadedFile(
            string temporaryPath,
            string targetPath,
            bool overwriteExisting)
        {
            if (!File.Exists(temporaryPath))
                throw new IOException("The completed cloud-function download file is missing.");

            if (File.Exists(targetPath))
            {
                if (!overwriteExisting)
                    throw new IOException($"Download target '{targetPath}' already exists.");
                File.Replace(temporaryPath, targetPath, null);
                return;
            }

            File.Move(temporaryPath, targetPath);
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup must not hide the original transfer result.
            }
        }

        private static PlayServFunctionResult MissingResponseFailure() =>
            Failure(
                null,
                new PlayServError(
                    PlayServErrorCode.InvalidResponse,
                    "function_response_missing",
                    "Cloud-function response is missing."));

        private static PlayServFunctionResult Failure(
            PlayServFunctionResponse response,
            PlayServError error) =>
            new PlayServFunctionResult(response, error);

        private static PlayServFunctionDownloadResult DownloadFailure(
            string filePath,
            PlayServFunctionResponse response,
            PlayServError error) =>
            new PlayServFunctionDownloadResult(filePath, 0, response, error);

        private static string NormalizeOptional(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "Cloud-function request failed.";
        }

        private sealed class TokenResolution
        {
            public TokenResolution(string token, PlayServError error)
            {
                Token = token;
                Error = error;
            }

            public string Token { get; }

            public PlayServError Error { get; }
        }

        private sealed class FunctionProgressAdapter : IProgress<PlayServRuntimeTransferProgress>
        {
            private readonly IProgress<PlayServFunctionTransferProgress> _target;

            public FunctionProgressAdapter(IProgress<PlayServFunctionTransferProgress> target)
            {
                _target = target;
            }

            public void Report(PlayServRuntimeTransferProgress value)
            {
                if (value == null)
                    return;
                _target.Report(new PlayServFunctionTransferProgress(
                    value.Direction == PlayServRuntimeTransferDirection.Upload
                        ? PlayServFunctionTransferDirection.Upload
                        : PlayServFunctionTransferDirection.Download,
                    value.BytesTransferred,
                    value.TotalBytes));
            }
        }
    }
}
