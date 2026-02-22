using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.Networking;

namespace Playserv.Deploy.Editor
{
    public sealed class DeploymentApiClient
    {
        private const string DeploymentPath = "/api/deployments";
        private readonly PlayServConfig _settings;

        public DeploymentApiClient(PlayServConfig settings)
        {
            _settings = settings;
        }

        public async Task UploadDeploymentAsync(string gameId, string zipPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.DeployApiEndpoint))
                throw new InvalidOperationException("DeploymentSettings.ApiEndpoint is empty.");

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            if (!File.Exists(zipPath))
                throw new FileNotFoundException("ZIP file not found.", zipPath);
            
            var url = BuildDeploymentUrl(_settings.DeployApiEndpoint);

            // Read ZIP bytes
            var data = File.ReadAllBytes(zipPath);

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(data);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/zip");
            req.SetRequestHeader("X-Game-Id", gameId);

            var authToken = ResolveDeployAuthToken();
            if (!string.IsNullOrWhiteSpace(authToken))
            {
                req.SetRequestHeader("Authorization", $"Bearer {authToken}");
            }

            req.timeout = Mathf.Max(1, _settings.TimeoutSeconds);

            var op = req.SendWebRequest();

            // Await UnityWebRequest in editor without coroutines
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(50, ct);
            }

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                throw new InvalidOperationException(BuildUploadErrorMessage(
                    req.responseCode,
                    req.error,
                    req.downloadHandler?.text,
                    url,
                    gameId));
            }
        }

        private static string BuildUploadErrorMessage(
            long responseCode,
            string unityError,
            string responseBody,
            string url,
            string gameId)
        {
            var details = ExtractErrorDetails(responseBody);
            var authHint = responseCode == 401
                ? " Configure a valid deploy bearer token in PlayServ config field 'deployAuthToken'."
                : string.Empty;

            var nativeAotHint = details.Any(line =>
                line.IndexOf("Native AOT compilation failed", StringComparison.OrdinalIgnoreCase) >= 0 &&
                line.Trim().EndsWith(":", StringComparison.Ordinal));

            var hint = nativeAotHint
                ? " Native AOT failed but server did not return compiler diagnostics. Check deployment-service logs; common causes are unsupported references (for example UnityEngine/UnityEditor) or code incompatible with AOT."
                : string.Empty;

            var detailText = details.Length > 0
                ? string.Join(" | ", details)
                : (string.IsNullOrWhiteSpace(responseBody) ? "<empty>" : responseBody);

            return
                $"Upload failed. HTTP {(int)responseCode}. Error: {unityError}.{authHint}" +
                $" Endpoint: {url}. GameId: {gameId}. Details: {detailText}.{hint}";
        }

        private static string[] ExtractErrorDetails(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return Array.Empty<string>();

            try
            {
                var token = JToken.Parse(responseBody);
                var obj = token as JObject;
                if (obj == null)
                    return new[] { responseBody };

                var messages = obj["errors"] is JArray errors
                    ? errors
                        .Select(x => x?.ToString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList()
                    : new System.Collections.Generic.List<string>();

                var topMessage = obj["message"]?.ToString();
                if (!string.IsNullOrWhiteSpace(topMessage))
                    messages.Insert(0, topMessage.Trim());

                return messages.Count > 0 ? messages.ToArray() : new[] { responseBody };
            }
            catch
            {
                return new[] { responseBody };
            }
        }

        private static string BuildDeploymentUrl(string endpoint)
        {
            endpoint = NormalizeEndpoint(endpoint);

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
                throw new InvalidOperationException($"DeploymentSettings.ApiEndpoint is invalid: {endpoint}");

            var builder = new UriBuilder(endpointUri);
            var path = builder.Path ?? string.Empty;

            if (string.IsNullOrEmpty(path) || path == "/")
            {
                builder.Path = DeploymentPath;
            }
            else
            {
                var normalizedPath = path.TrimEnd('/');
                if (!normalizedPath.EndsWith(DeploymentPath, StringComparison.OrdinalIgnoreCase))
                    builder.Path = normalizedPath + DeploymentPath;
            }

            return builder.Uri.ToString();
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            return endpoint?.Trim() ?? string.Empty;
        }

        private string ResolveDeployAuthToken()
        {
            if (!string.IsNullOrWhiteSpace(_settings.DeployAuthToken))
                return _settings.DeployAuthToken.Trim();

            return string.Empty;
        }
    }
}
