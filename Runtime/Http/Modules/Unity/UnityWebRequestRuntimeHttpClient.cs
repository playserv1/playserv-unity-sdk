#if UNITY_5_3_OR_NEWER
using System;
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

        private readonly PlayServRuntimeSettings _settings;

        public UnityWebRequestRuntimeHttpClient(PlayServRuntimeSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);

            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Latest version response body is empty.");

            var version = JsonResponseReader.GetStringValueIgnoreCase(body, "version");

            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("Latest version was not found in response.");

            PlayServLog.Trace(PlayServLogCategory.Http, $"Latest game version response parsed. version={version}");
            return version;
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

        private void AddCommonHeaders(UnityWebRequest req)
        {
            var authToken = ResolveDeployAuthToken();
            if (!string.IsNullOrWhiteSpace(authToken))
                req.SetRequestHeader("Authorization", $"Bearer {authToken}");
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

        private string ResolveDeployAuthToken()
        {
            return string.IsNullOrWhiteSpace(_settings.DeployAuthToken)
                ? string.Empty
                : _settings.DeployAuthToken.Trim();
        }
    }
}
#endif
