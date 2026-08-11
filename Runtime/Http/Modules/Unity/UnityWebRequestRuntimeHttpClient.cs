#if UNITY_5_3_OR_NEWER
using System;
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
    internal sealed class UnityWebRequestRuntimeHttpClient : IPlayServRuntimeHttpClient
    {
        private const string ApiPath = "/api";
        private const string DeploymentEndpointPath = "/deployments";
        private const string LatestVersionPathTemplate = "games/{0}/version/latest";
        private const string ClientHeaderName = "X-Playserv-Client";
        private const string AnonSignInPath = "auth/players/anon";
        private const string RefreshPath = "auth/players/refresh";

        private readonly PlayServRuntimeSettings _settings;
        private readonly IJsonCodec _jsonCodec;

        public UnityWebRequestRuntimeHttpClient(PlayServRuntimeSettings settings, IJsonCodec jsonCodec = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _jsonCodec = jsonCodec;
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
            CancellationToken ct = default)
        {
            var normalizedClientToken = NormalizePublicClientToken(clientToken);

            var url = BuildAuthUrl(_settings.BackendServerAddress, AnonSignInPath);
            PlayServLog.Trace(PlayServLogCategory.Http, $"Requesting anonymous player sign-in. url={url}");

            using var req = CreateAuthPostRequest(url, normalizedClientToken, "{}");
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

        [Serializable]
        private sealed class PlayerRefreshRequestBody
        {
            public string refresh_token;
        }

        private static UnityWebRequest CreateAuthPostRequest(
            string url,
            string clientToken,
            string jsonBody)
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
            return req;
        }

        private string SerializeJson(object value)
        {
            return _jsonCodec == null
                ? JsonUtility.ToJson(value)
                : _jsonCodec.Serialize(value);
        }

        private T ParseJsonResponse<T>(UnityWebRequest req, string operationLabel)
        {
            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException($"{operationLabel} response body is empty.");

            var parsed = _jsonCodec == null
                ? JsonUtility.FromJson<T>(body)
                : _jsonCodec.Deserialize<T>(body);
            if (parsed == null)
                throw new InvalidOperationException($"{operationLabel} response could not be parsed.");

            return parsed;
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

        private async Task SendRequestAsync(UnityWebRequest req, CancellationToken ct)
        {
            if (req == null)
                throw new ArgumentNullException(nameof(req));

            var timeoutSeconds = Mathf.Max(1, _settings.TimeoutSeconds);
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
                var detailText = string.IsNullOrWhiteSpace(body) ? req.error : body;
                throw new InvalidOperationException(
                    $"Request failed. HTTP {(int)req.responseCode}. Endpoint: {req.url}. Details: {detailText}");
            }
        }

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

    }
}
#endif
