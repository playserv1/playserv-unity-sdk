using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.Networking;

namespace Playserv.Deploy.Editor
{
    public sealed class DeploymentApiClient
    {
        private const string DeploymentPath = "/api/deployments";
        private const string LegacyLocalDeployEndpoint = "http://localhost:5000/api/deployments";
        private const string DefaultBackofficeDeployEndpoint = "https://playserv-backoffice.test.playserv.io/api/deployments";
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

            // Server-side deployment service reads game id from X-Game-Id header.
            // Keep query parameter as a compatibility fallback for older services.
            var url = BuildDeploymentUrl(_settings.DeployApiEndpoint, gameId);

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
                if (req.responseCode == 401)
                {
                    throw new InvalidOperationException(
                        $"Upload failed. HTTP 401 Unauthorized. " +
                        $"Configure a valid deploy bearer token in PlayServ config field 'deployAuthToken'. " +
                        $"Body: {req.downloadHandler?.text}");
                }

                throw new InvalidOperationException(
                    $"Upload failed. HTTP {(int)req.responseCode}. Error: {req.error}. Body: {req.downloadHandler?.text}"
                );
            }
        }

        private static string BuildDeploymentUrl(string endpoint, string gameId)
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

            var gameIdParam = $"gameId={UnityWebRequest.EscapeURL(gameId)}";
            var query = builder.Query.TrimStart('?');
            builder.Query = string.IsNullOrEmpty(query) ? gameIdParam : $"{query}&{gameIdParam}";

            return builder.Uri.ToString();
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            var value = endpoint?.Trim() ?? string.Empty;
            if (string.Equals(value, LegacyLocalDeployEndpoint, StringComparison.OrdinalIgnoreCase))
                return DefaultBackofficeDeployEndpoint;

            return value;
        }

        private string ResolveDeployAuthToken()
        {
            if (!string.IsNullOrWhiteSpace(_settings.DeployAuthToken))
                return _settings.DeployAuthToken.Trim();

            return _settings.GameAccessToken?.Trim() ?? string.Empty;
        }
    }
}
