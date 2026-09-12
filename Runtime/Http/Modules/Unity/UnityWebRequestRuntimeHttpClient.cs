#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using UnityEngine;
using UnityEngine.Networking;

namespace Playserv.Http.Modules.Unity
{
    internal sealed class UnityWebRequestRuntimeHttpClient :
        IPlayServRuntimeHttpClient,
        IPlayServRuntimeBinaryHttpClient,
        IPlayServPlayerIdentityHttpClient
    {
        private const string ApiPath = "/api";
        private const string DeploymentEndpointPath = "/deployments";
        private const string LatestVersionPathTemplate = "games/{0}/version/latest";
        private const string ClientHeaderName = "X-Playserv-Client";
        private const string FunctionVersionHeaderName = "X-Playserv-Function-Version";
        private const string AnonSignInPath = "auth/players/anon";
        private const string RefreshPath = "auth/players/refresh";
        private const string ExternalLoginPath = "auth/players/login";
        private const string SignOutPath = "auth/players/sign-out";
        private const string ProvidersPath = "auth/players/providers";
        private const string LinkPath = "auth/players/link";
        private const string UnlinkPath = "auth/players/unlink";
        private const string MergePath = "auth/players/merge";

        private readonly PlayServRuntimeSettings _settings;
        private readonly IJsonCodec _jsonCodec;

        public UnityWebRequestRuntimeHttpClient(PlayServRuntimeSettings settings, IJsonCodec jsonCodec = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _jsonCodec = jsonCodec ?? new NewtonsoftJsonCodec();
        }

        public async Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.DeployApiServerAddress))
                throw new InvalidOperationException("PlayServRuntimeSettings.DeployApiServerAddress is empty.");

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            var url = BuildApiRelativeUrl(_settings.DeployApiServerAddress, BuildPath(LatestVersionPathTemplate, gameId));
            PlayServLog.Trace(PlayServLogCategory.Http, $"Requesting latest game version. url={url}");

            using var req = UnityWebRequest.Get(url);
            await SendRequestAsync(req, ct);

            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Latest version response body is empty.");

            var version = JsonResponseReader.GetStringValueIgnoreCase(body, "version", _jsonCodec);

            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("Latest version was not found in response.");

            PlayServLog.Trace(PlayServLogCategory.Http, $"Latest game version response parsed. version={version}");
            return version;
        }

        public async Task<PlayerTokenBundleDto> SignInAnonAsync(
            string clientToken,
            CancellationToken ct = default) =>
            await SignInAnonAsync(clientToken, fingerprint: null, ct: ct);

        public async Task<PlayerTokenBundleDto> SignInAnonAsync(
            string clientToken,
            PlayerFingerprintDto fingerprint,
            CancellationToken ct = default)
        {
            var normalizedClientToken = NormalizePublicClientToken(clientToken);

            var url = BuildAuthUrl(_settings.BackendServerAddress, AnonSignInPath);
            PlayServLog.Trace(PlayServLogCategory.Http, $"Requesting anonymous player sign-in. url={url}");

            var body = fingerprint == null
                ? "{}"
                : SerializeJson(new PlayerAnonymousLoginRequestDto { fingerprint = fingerprint });
            using var req = CreateAuthPostRequest(url, normalizedClientToken, body);
            await SendRequestAsync(req, ct);

            var bundle = ParseJsonResponse<PlayerTokenBundleDto>(req, "Anonymous sign-in");
            if (string.IsNullOrWhiteSpace(bundle.access_token) ||
                string.IsNullOrWhiteSpace(bundle.refresh_token) ||
                string.IsNullOrWhiteSpace(bundle.player_id))
            {
                throw new InvalidOperationException(
                    "Anonymous sign-in response did not contain a complete player token bundle.");
            }

            return bundle;
        }

        public async Task<PlayerRefreshResponseDto> RefreshAsync(
            string clientToken,
            string refreshToken,
            CancellationToken ct = default)
        {
            var normalizedClientToken = NormalizePublicClientToken(clientToken);

            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentException("Refresh token is required.", nameof(refreshToken));

            var url = BuildAuthUrl(_settings.BackendServerAddress, RefreshPath);
            PlayServLog.Trace(PlayServLogCategory.Http, $"Requesting player token refresh. url={url}");

            var requestBody = SerializeJson(new PlayerRefreshRequestBody
            {
                refresh_token = refreshToken.Trim()
            });
            using var req = CreateAuthPostRequest(url, normalizedClientToken, requestBody);
            await SendRequestAsync(req, ct);

            var refreshed = ParseJsonResponse<PlayerRefreshResponseDto>(req, "Player token refresh");
            if (string.IsNullOrWhiteSpace(refreshed.access_token) ||
                string.IsNullOrWhiteSpace(refreshed.refresh_token))
            {
                throw new InvalidOperationException(
                    "Player token refresh response did not contain access and refresh tokens.");
            }

            return refreshed;
        }

        public async Task<PlayerTokenBundleDto> LoginExternalAsync(
            string clientToken,
            PlayerExternalLoginRequestDto request,
            string playerAccessToken = null,
            CancellationToken ct = default)
        {
            var normalizedClientToken = NormalizePublicClientToken(clientToken);
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.provider))
                throw new ArgumentException("External identity provider is required.", nameof(request));

            if (string.IsNullOrWhiteSpace(request.provider_token))
                throw new ArgumentException("External provider token is required.", nameof(request));

            var url = BuildAuthUrl(_settings.BackendServerAddress, ExternalLoginPath);
            PlayServLog.Trace(PlayServLogCategory.Http, $"Requesting external player login. provider={request.provider}, url={url}");

            using var req = CreateAuthPostRequest(
                url,
                normalizedClientToken,
                SerializeJson(request),
                playerAccessToken);
            await SendRequestAsync(req, ct);

            var bundle = ParseJsonResponse<PlayerTokenBundleDto>(req, "External player login");
            if (string.IsNullOrWhiteSpace(bundle.access_token) ||
                string.IsNullOrWhiteSpace(bundle.refresh_token) ||
                string.IsNullOrWhiteSpace(bundle.player_id))
            {
                throw new InvalidOperationException(
                    "External player login response did not contain a complete player token bundle.");
            }

            return bundle;
        }

        public async Task<PlayerAuthProvidersProbeDto> GetAuthProvidersAsync(
            string clientToken,
            CancellationToken ct = default)
        {
            var normalizedClientToken = NormalizePublicClientToken(clientToken);
            var url = BuildAuthUrl(_settings.BackendServerAddress, ProvidersPath);
            PlayServLog.Trace(PlayServLogCategory.Http, $"Requesting player auth providers. url={url}");

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader(ClientHeaderName, normalizedClientToken);
            await SendRequestAsync(req, ct);
            return ParseJsonResponse<PlayerAuthProvidersProbeDto>(req, "Player auth providers");
        }

        public async Task<PlayerTokenBundleDto> LinkIdentityAsync(
            string clientToken,
            PlayerLinkRequestDto request,
            string playerAccessToken,
            CancellationToken ct = default)
        {
            ValidateIdentityRequest(request?.provider, request?.provider_token, request);
            var url = BuildAuthUrl(_settings.BackendServerAddress, LinkPath);
            using var req = CreateAuthPostRequest(
                url,
                NormalizePublicClientToken(clientToken),
                SerializeJson(request),
                RequirePlayerAccessToken(playerAccessToken));
            await SendRequestAsync(req, ct);
            return ParseTokenBundle(req, "Provider link");
        }

        public async Task UnlinkIdentityAsync(
            string clientToken,
            PlayerUnlinkRequestDto request,
            string playerAccessToken,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.provider))
                throw new ArgumentException("Identity provider is required.", nameof(request));

            var url = BuildAuthUrl(_settings.BackendServerAddress, UnlinkPath);
            using var req = CreateAuthPostRequest(
                url,
                NormalizePublicClientToken(clientToken),
                SerializeJson(request),
                RequirePlayerAccessToken(playerAccessToken));
            await SendRequestAsync(req, ct);
        }

        public async Task<PlayerTokenBundleDto> MergeIdentityAsync(
            string clientToken,
            PlayerMergeRequestDto request,
            string playerAccessToken,
            CancellationToken ct = default)
        {
            ValidateIdentityRequest(request?.provider, request?.provider_token, request);
            if (string.IsNullOrWhiteSpace(request.primary_plr_id) ||
                string.IsNullOrWhiteSpace(request.absorbed_plr_id))
            {
                throw new ArgumentException("Primary and absorbed player IDs are required.", nameof(request));
            }

            var url = BuildAuthUrl(_settings.BackendServerAddress, MergePath);
            using var req = CreateAuthPostRequest(
                url,
                NormalizePublicClientToken(clientToken),
                SerializeJson(request),
                RequirePlayerAccessToken(playerAccessToken));
            await SendRequestAsync(req, ct);
            return ParseTokenBundle(req, "Player identity merge");
        }

        public async Task SignOutAsync(
            string clientToken,
            string refreshToken,
            CancellationToken ct = default)
        {
            var normalizedClientToken = NormalizePublicClientToken(clientToken);
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentException("Refresh token is required.", nameof(refreshToken));

            var url = BuildAuthUrl(_settings.BackendServerAddress, SignOutPath);
            PlayServLog.Trace(PlayServLogCategory.Http, $"Requesting player sign-out. url={url}");

            var requestBody = SerializeJson(new PlayerRefreshRequestBody
            {
                refresh_token = refreshToken.Trim()
            });
            using var req = CreateAuthPostRequest(url, normalizedClientToken, requestBody);
            await SendRequestAsync(req, ct);
        }

        public async Task<PlayServRuntimeDataResponse> SendDataAsync(
            PlayServRuntimeDataRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var clientToken = request.RequiresClientToken
                ? NormalizePublicClientToken(request.ClientToken)
                : null;
            if (string.IsNullOrWhiteSpace(request.RelativePath))
                throw new ArgumentException("Runtime data request path is required.", nameof(request));

            var method = string.IsNullOrWhiteSpace(request.Method)
                ? "GET"
                : request.Method.Trim().ToUpperInvariant();
            if (method != "GET" && method != "POST" && method != "PUT" && method != "PATCH" && method != "DELETE")
                throw new ArgumentException($"Unsupported runtime data HTTP method '{method}'.", nameof(request));

            var url = BuildAuthUrl(_settings.BackendServerAddress, request.RelativePath);
            using var req = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            if (request.JsonBody != null)
            {
                if (!string.IsNullOrWhiteSpace(request.ContentType))
                    ValidateHeaderValue("Content-Type", request.ContentType);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.JsonBody))
                {
                    contentType = string.IsNullOrWhiteSpace(request.ContentType)
                        ? "application/json"
                        : request.ContentType.Trim()
                };
            }

            ApplyRuntimeRequestHeaders(req, request, clientToken);

            await SendRequestAsync(req, ct, request.TimeoutSeconds);
            var responseHeaders = req.GetResponseHeaders();
            return new PlayServRuntimeDataResponse(
                (int)req.responseCode,
                req.downloadHandler?.text,
                req.GetResponseHeader("ETag"),
                req.GetResponseHeader("Location"),
                req.GetResponseHeader("Content-Type"),
                responseHeaders == null
                    ? null
                    : new Dictionary<string, string>(responseHeaders, StringComparer.OrdinalIgnoreCase),
                req.downloadHandler?.data);
        }

        public async Task<PlayServRuntimeDataResponse> SendBinaryDataAsync(
            PlayServRuntimeBinaryDataRequest binaryRequest,
            CancellationToken ct = default)
        {
            if (binaryRequest == null)
                throw new ArgumentNullException(nameof(binaryRequest));
            if (binaryRequest.Request == null)
                throw new ArgumentException("A runtime request is required.", nameof(binaryRequest));
            if (binaryRequest.MaxResponseBytes <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(binaryRequest),
                    "Maximum response bytes must be positive.");

            var request = binaryRequest.Request;
            var clientToken = request.RequiresClientToken
                ? NormalizePublicClientToken(request.ClientToken)
                : null;
            if (string.IsNullOrWhiteSpace(request.RelativePath))
                throw new ArgumentException("Runtime data request path is required.", nameof(binaryRequest));

            var method = string.IsNullOrWhiteSpace(request.Method)
                ? "GET"
                : request.Method.Trim().ToUpperInvariant();
            if (method != "GET" && method != "POST" && method != "PUT" && method != "PATCH" && method != "DELETE")
                throw new ArgumentException($"Unsupported runtime data HTTP method '{method}'.", nameof(binaryRequest));

            var bodyBytes = binaryRequest.BodyBytes == null
                ? null
                : (byte[])binaryRequest.BodyBytes.Clone();
            var url = BuildAuthUrl(_settings.BackendServerAddress, request.RelativePath);
            var handler = new BoundedBinaryDownloadHandler(
                binaryRequest.MaxResponseBytes,
                binaryRequest.DownloadFilePath);
            try
            {
                using var req = new UnityWebRequest(url, method)
                {
                    downloadHandler = handler
                };
                if (bodyBytes != null)
                {
                    if (!string.IsNullOrWhiteSpace(request.ContentType))
                        ValidateHeaderValue("Content-Type", request.ContentType);
                    req.uploadHandler = new UploadHandlerRaw(bodyBytes)
                    {
                        contentType = string.IsNullOrWhiteSpace(request.ContentType)
                            ? "application/octet-stream"
                            : request.ContentType.Trim()
                    };
                }

                ApplyRuntimeRequestHeaders(req, request, clientToken);
                await SendBinaryRequestAsync(
                    req,
                    handler,
                    bodyBytes == null ? (long?)null : bodyBytes.LongLength,
                    binaryRequest.Progress,
                    ct,
                    request.TimeoutSeconds);

                var responseHeaders = req.GetResponseHeaders();
                return new PlayServRuntimeDataResponse(
                    (int)req.responseCode,
                    handler.GetBodyText(),
                    req.GetResponseHeader("ETag"),
                    req.GetResponseHeader("Location"),
                    req.GetResponseHeader("Content-Type"),
                    responseHeaders == null
                        ? null
                        : new Dictionary<string, string>(responseHeaders, StringComparer.OrdinalIgnoreCase),
                    handler.GetBodyBytes(),
                    handler.BytesReceived);
            }
            finally
            {
                handler.CloseStream();
            }
        }

        private static void ApplyRuntimeRequestHeaders(
            UnityWebRequest req,
            PlayServRuntimeDataRequest request,
            string clientToken)
        {
            if (!string.IsNullOrWhiteSpace(clientToken))
                req.SetRequestHeader(ClientHeaderName, clientToken);
            if (!string.IsNullOrWhiteSpace(request.BearerToken))
            {
                req.SetRequestHeader(
                    "Authorization",
                    PlayServCredentialPolicy.NormalizePlayerAuthorization(request.BearerToken));
            }
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
                req.SetRequestHeader("Idempotency-Key", request.IdempotencyKey);
            if (!string.IsNullOrWhiteSpace(request.IfMatch))
                req.SetRequestHeader("If-Match", request.IfMatch.Trim());
            if (!string.IsNullOrWhiteSpace(request.FunctionVersion))
            {
                ValidateHeaderValue(FunctionVersionHeaderName, request.FunctionVersion);
                req.SetRequestHeader(FunctionVersionHeaderName, request.FunctionVersion.Trim());
            }
            if (request.Headers == null)
                return;
            foreach (var header in request.Headers)
            {
                ValidateCustomHeader(header.Key, header.Value);
                req.SetRequestHeader(header.Key.Trim(), header.Value);
            }
        }

        private async Task SendBinaryRequestAsync(
            UnityWebRequest req,
            BoundedBinaryDownloadHandler handler,
            long? uploadBytes,
            IProgress<PlayServRuntimeTransferProgress> progress,
            CancellationToken ct,
            int? timeoutSecondsOverride)
        {
            var timeoutSeconds = Mathf.Max(1, timeoutSecondsOverride ?? _settings.TimeoutSeconds);
            req.timeout = timeoutSeconds;
            progress?.Report(new PlayServRuntimeTransferProgress(
                PlayServRuntimeTransferDirection.Upload,
                0,
                uploadBytes));
            progress?.Report(new PlayServRuntimeTransferProgress(
                PlayServRuntimeTransferDirection.Download,
                0,
                null));

            var operation = req.SendWebRequest();
            long lastUploaded = -1;
            long lastDownloaded = -1;
            while (!operation.isDone)
            {
                if (ct.IsCancellationRequested)
                {
                    TryAbort(req);
                    ct.ThrowIfCancellationRequested();
                }

                var uploaded = (long)req.uploadedBytes;
                var downloaded = handler.BytesReceived;
                if (uploaded != lastUploaded)
                {
                    progress?.Report(new PlayServRuntimeTransferProgress(
                        PlayServRuntimeTransferDirection.Upload,
                        uploaded,
                        uploadBytes));
                    lastUploaded = uploaded;
                }
                if (downloaded != lastDownloaded)
                {
                    progress?.Report(new PlayServRuntimeTransferProgress(
                        PlayServRuntimeTransferDirection.Download,
                        downloaded,
                        handler.ContentLength));
                    lastDownloaded = downloaded;
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                await Task.Yield();
#else
                await Task.Delay(50);
#endif
            }

            progress?.Report(new PlayServRuntimeTransferProgress(
                PlayServRuntimeTransferDirection.Upload,
                uploadBytes ?? (long)req.uploadedBytes,
                uploadBytes));
            progress?.Report(new PlayServRuntimeTransferProgress(
                PlayServRuntimeTransferDirection.Download,
                handler.BytesReceived,
                handler.ContentLength));

            if (handler.LimitExceeded)
            {
                throw new PlayServRuntimeHttpException(
                    "Cloud-function response exceeded the configured response-size limit.",
                    (int)req.responseCode,
                    string.Empty,
                    "function_response_too_large",
                    false);
            }
            if (handler.WriteException != null)
                throw new IOException("Cloud-function response could not be written.", handler.WriteException);

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                var bodyBytes = handler.GetBodyBytes();
                var body = handler.GetBodyText();
                var problem = ParseProblemDetails(body);
                var detailText = !string.IsNullOrWhiteSpace(problem?.detail)
                    ? problem.detail
                    : req.error;
                if (string.IsNullOrWhiteSpace(detailText))
                    detailText = "The server returned an error response.";
#if UNITY_2020_2_OR_NEWER
                var isNetworkError = req.result == UnityWebRequest.Result.ConnectionError;
#else
                var isNetworkError = req.isNetworkError;
#endif
                throw new PlayServRuntimeHttpException(
                    $"Request failed. HTTP {(int)req.responseCode}. Endpoint: {req.url}. Details: {detailText}",
                    (int)req.responseCode,
                    body,
                    FirstNonEmpty(problem?.code, problem?.error),
                    isNetworkError,
                    problemTitle: problem?.title,
                    problemDetail: problem?.detail,
                    responseBytes: bodyBytes);
            }
        }

        [Serializable]
        private sealed class PlayerRefreshRequestBody
        {
            public string refresh_token;
        }

        private static UnityWebRequest CreateAuthPostRequest(
            string url,
            string clientToken,
            string jsonBody,
            string playerAccessToken = null)
        {
            var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody))
                {
                    contentType = "application/json"
                },
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader(ClientHeaderName, clientToken);
            if (!string.IsNullOrWhiteSpace(playerAccessToken))
            {
                req.SetRequestHeader(
                    "Authorization",
                    PlayServCredentialPolicy.NormalizePlayerAuthorization(playerAccessToken));
            }
            return req;
        }

        private string SerializeJson(object value)
        {
            return _jsonCodec.Serialize(value);
        }

        private T ParseJsonResponse<T>(UnityWebRequest req, string operationLabel)
        {
            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException($"{operationLabel} response body is empty.");

            var parsed = _jsonCodec.Deserialize<T>(body);
            if (parsed == null)
                throw new InvalidOperationException($"{operationLabel} response could not be parsed.");

            return parsed;
        }

        private PlayerTokenBundleDto ParseTokenBundle(UnityWebRequest req, string operationLabel)
        {
            var bundle = ParseJsonResponse<PlayerTokenBundleDto>(req, operationLabel);
            if (string.IsNullOrWhiteSpace(bundle.access_token) ||
                string.IsNullOrWhiteSpace(bundle.refresh_token) ||
                string.IsNullOrWhiteSpace(bundle.player_id))
            {
                throw new InvalidOperationException($"{operationLabel} response did not contain a complete player token bundle.");
            }
            return bundle;
        }

        private static void ValidateIdentityRequest(string provider, string providerToken, object request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("Identity provider is required.", nameof(request));
            if (string.IsNullOrWhiteSpace(providerToken))
                throw new ArgumentException("Provider token is required.", nameof(request));
        }

        private static string RequirePlayerAccessToken(string playerAccessToken)
        {
            if (string.IsNullOrWhiteSpace(playerAccessToken))
                throw new ArgumentException("A player access token is required.", nameof(playerAccessToken));
            return playerAccessToken;
        }

        private static string BuildAuthUrl(string backendServerAddress, string relativePath)
        {
            var baseUrl = BuildAuthBaseUrl(backendServerAddress);
            var rel = (relativePath ?? string.Empty).TrimStart('/');
            return string.Concat(baseUrl, "/", rel);
        }

        private static string BuildAuthBaseUrl(string backendServerAddress)
        {
            var endpoint = NormalizeEndpoint(backendServerAddress);
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("PlayServRuntimeSettings.BackendServerAddress is empty.");

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            {
                throw new InvalidOperationException(
                    $"PlayServRuntimeSettings.BackendServerAddress is invalid: {endpoint}");
            }

            string httpScheme;
            if (string.Equals(endpointUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
                httpScheme = Uri.UriSchemeHttps;
            else if (string.Equals(endpointUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
                httpScheme = Uri.UriSchemeHttp;
            else if (string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                httpScheme = endpointUri.Scheme;
            else
                throw new InvalidOperationException("Player authentication requires an http, https, ws, or wss backend endpoint.");

            var builder = new UriBuilder(endpointUri)
            {
                Scheme = httpScheme,
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string BuildApiBaseUrl(string serverAddress)
        {
            serverAddress = NormalizeEndpoint(serverAddress);

            if (!Uri.TryCreate(serverAddress, UriKind.Absolute, out var endpointUri))
                throw new InvalidOperationException($"PlayServRuntimeSettings.DeployApiServerAddress is invalid: {serverAddress}");

            var builder = new UriBuilder(endpointUri);
            var normalizedPath = (builder.Path ?? string.Empty).TrimEnd('/');

            if (string.IsNullOrEmpty(normalizedPath))
            {
                builder.Path = ApiPath;
            }
            else
            {
                if (normalizedPath.EndsWith(DeploymentEndpointPath, StringComparison.OrdinalIgnoreCase))
                    normalizedPath = normalizedPath.Substring(0, normalizedPath.Length - DeploymentEndpointPath.Length);

                if (!normalizedPath.EndsWith(ApiPath, StringComparison.OrdinalIgnoreCase))
                    normalizedPath += ApiPath;

                builder.Path = normalizedPath;
            }

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string BuildApiRelativeUrl(string serverAddress, string relativePath)
        {
            var baseUrl = BuildApiBaseUrl(serverAddress);
            var rel = (relativePath ?? string.Empty).TrimStart('/');
            return string.Concat(baseUrl, "/", rel);
        }

        private static string BuildPath(string template, string gameId)
        {
            return string.Format(template, gameId);
        }

        private async Task SendRequestAsync(
            UnityWebRequest req,
            CancellationToken ct,
            int? timeoutSecondsOverride = null)
        {
            if (req == null)
                throw new ArgumentNullException(nameof(req));

            var timeoutSeconds = Mathf.Max(1, timeoutSecondsOverride ?? _settings.TimeoutSeconds);
            req.timeout = timeoutSeconds;
            var op = req.SendWebRequest();
            
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested)
                {
                    TryAbort(req);
                    PlayServLog.TraceWarning(PlayServLogCategory.Http, $"Runtime API request cancelled before completion. url={req.url}");
                    ct.ThrowIfCancellationRequested();
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                await Task.Yield();
#else
                await Task.Delay(50, ct);
#endif
            }

            PlayServLog.Trace(
                PlayServLogCategory.Http,
                $"Runtime API request completed. code={req.responseCode}, error={req.error ?? "<none>"}, url={req.url}");

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                var body = req.downloadHandler?.text;
                var problem = ParseProblemDetails(body);
                var detailText = !string.IsNullOrWhiteSpace(problem?.detail)
                    ? problem.detail
                    : req.error;
                if (string.IsNullOrWhiteSpace(detailText))
                    detailText = "The server returned an error response.";
#if UNITY_2020_2_OR_NEWER
                var isNetworkError = req.result == UnityWebRequest.Result.ConnectionError;
#else
                var isNetworkError = req.isNetworkError;
#endif
                throw new PlayServRuntimeHttpException(
                    $"Request failed. HTTP {(int)req.responseCode}. Endpoint: {req.url}. Details: {detailText}",
                    (int)req.responseCode,
                    body,
                    FirstNonEmpty(problem?.code, problem?.error),
                    isNetworkError,
                    problemTitle: problem?.title,
                    problemDetail: problem?.detail,
                    responseBytes: req.downloadHandler?.data);
            }
        }

        private PlayServProblemDetailsDto ParseProblemDetails(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                return _jsonCodec.Deserialize<PlayServProblemDetailsDto>(body);
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonEmpty(string first, string second) =>
            !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;

        private static void TryAbort(UnityWebRequest req)
        {
            try
            {
                req.Abort();
            }
            catch
            {
                // ignored
            }
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            return endpoint?.Trim() ?? string.Empty;
        }

        private static string NormalizePublicClientToken(string clientToken)
        {
            if (string.IsNullOrWhiteSpace(clientToken))
                throw new ArgumentException("Client token is required.", nameof(clientToken));

            var normalized = clientToken.Trim();
            if (normalized.IndexOf('\r') >= 0 || normalized.IndexOf('\n') >= 0)
                throw new InvalidOperationException("PlayServ credentials cannot contain line breaks.");

            if (!normalized.StartsWith("pk_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player authentication requires a public PlayServ pk_* client token.");
            }

            return normalized;
        }

        private static void ValidateCustomHeader(string name, string value)
        {
            var normalizedName = (name ?? string.Empty).Trim();
            if (normalizedName.Length == 0)
                throw new ArgumentException("Runtime request header names cannot be empty.", nameof(name));
            foreach (var character in normalizedName)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '!' && character != '#' && character != '$' &&
                    character != '%' && character != '&' && character != '\'' &&
                    character != '*' && character != '+' && character != '-' &&
                    character != '.' && character != '^' && character != '_' &&
                    character != '`' && character != '|' && character != '~')
                {
                    throw new ArgumentException($"Runtime request header name '{normalizedName}' is invalid.", nameof(name));
                }
            }

            if (string.Equals(normalizedName, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedName, ClientHeaderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedName, "Host", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedName, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedName, "Connection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                normalizedName.StartsWith("X-Playserv-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Runtime request header '{normalizedName}' is reserved by PlayServ.",
                    nameof(name));
            }

            ValidateHeaderValue(normalizedName, value);
        }

        private static void ValidateHeaderValue(string name, string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value), $"Runtime request header '{name}' cannot be null.");
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException($"Runtime request header '{name}' cannot contain line breaks.", nameof(value));
        }

        private sealed class BoundedBinaryDownloadHandler : DownloadHandlerScript
        {
            private const int BufferSize = 32 * 1024;
            private readonly long _maxBytes;
            private readonly MemoryStream _memory;
            private readonly FileStream _file;
            private bool _closed;

            public BoundedBinaryDownloadHandler(long maxBytes, string filePath)
                : base(new byte[BufferSize])
            {
                _maxBytes = maxBytes;
                if (string.IsNullOrWhiteSpace(filePath))
                    _memory = new MemoryStream();
                else
                    _file = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }

            public long BytesReceived { get; private set; }

            public long? ContentLength { get; private set; }

            public bool LimitExceeded { get; private set; }

            public Exception WriteException { get; private set; }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                ContentLength = contentLength <= long.MaxValue ? (long)contentLength : (long?)null;
                if (!ContentLength.HasValue || ContentLength.Value > _maxBytes)
                    LimitExceeded = true;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                    return true;
                if (LimitExceeded || BytesReceived > _maxBytes - dataLength)
                {
                    LimitExceeded = true;
                    return false;
                }

                try
                {
                    if (_file != null)
                        _file.Write(data, 0, dataLength);
                    else
                        _memory.Write(data, 0, dataLength);
                    BytesReceived += dataLength;
                    return true;
                }
                catch (Exception exception)
                {
                    WriteException = exception;
                    return false;
                }
            }

            protected override void CompleteContent()
            {
                try
                {
                    _file?.Flush();
                }
                catch (Exception exception)
                {
                    WriteException = WriteException ?? exception;
                }
            }

            public byte[] GetBodyBytes() =>
                _memory == null ? Array.Empty<byte>() : _memory.ToArray();

            public string GetBodyText()
            {
                var bytes = GetBodyBytes();
                return bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
            }

            public void CloseStream()
            {
                if (_closed)
                    return;
                _closed = true;
                try
                {
                    _file?.Dispose();
                }
                finally
                {
                    _memory?.Dispose();
                }
            }
        }

    }
}
#endif
