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
        private const string ApiPath = "/api";
        private const string DeploymentsPath = "/deployments";
        private const string DeploymentPath = ApiPath + DeploymentsPath;
        private const string SchemasLatestPathTemplate = "schemas/{0}/latest";
        private const string RemoteCodeHashPathTemplate = "games/{0}/rpc-code/hash";
        private const string LatestVersionPathTemplate = "games/{0}/version/latest";
        private const string CodeArchivePathTemplate = "games/{0}/code-archive";
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

        public async Task FetchLatestSchemasAsync(string gameId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            var url = BuildApiUrl(BuildSchemasLatestPath(gameId));

            using var req = UnityWebRequest.Get(url);
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);
        }

        public async Task<string> GetRemoteCodeHashAsync(string gameId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            var url = BuildApiUrl(BuildPath(RemoteCodeHashPathTemplate, gameId));

            using var req = UnityWebRequest.Get(url);
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);

            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Remote hash response body is empty.");

            var obj = JObject.Parse(body);
            var hash = obj["hash"]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(hash))
                throw new InvalidOperationException("Remote hash was not found in response.");

            return hash;
        }

        public async Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            var url = BuildApiUrl(BuildPath(LatestVersionPathTemplate, gameId));

            using var req = UnityWebRequest.Get(url);
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);

            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Latest version response body is empty.");

            var obj = JObject.Parse(body);
            var version = obj["version"]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("Latest version was not found in response.");

            return version;
        }

        public async Task<string> DownloadCodeArchiveAsync(string gameId, string outputDirectory, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

            var url = BuildApiUrl(BuildPath(CodeArchivePathTemplate, gameId));

            using var req = UnityWebRequest.Get(url);
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);

            Directory.CreateDirectory(outputDirectory);

            var archivePath = Path.Combine(
                outputDirectory,
                $"rpc-code-archive_{gameId}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");

            var data = req.downloadHandler?.data;
            if (data == null || data.Length == 0)
                throw new InvalidOperationException("Downloaded archive is empty.");

            File.WriteAllBytes(archivePath, data);
            return archivePath;
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
            var normalizedPath = (builder.Path ?? string.Empty).TrimEnd('/');

            if (string.IsNullOrEmpty(normalizedPath))
            {
                builder.Path = DeploymentPath;
            }
            else
            {
                if (!normalizedPath.EndsWith(DeploymentPath, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Path = normalizedPath.EndsWith(ApiPath, StringComparison.OrdinalIgnoreCase)
                        ? normalizedPath + DeploymentsPath
                        : normalizedPath + DeploymentPath;
                }
            }

            return builder.Uri.ToString();
        }

        private string BuildApiUrl(string relativePath)
        {
            var deploymentUrl = BuildDeploymentUrl(_settings.DeployApiEndpoint);
            var deploymentUri = new Uri(deploymentUrl, UriKind.Absolute);
            var builder = new UriBuilder(deploymentUri);
            var path = (builder.Path ?? string.Empty).TrimEnd('/');

            if (path.EndsWith(DeploymentsPath, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - DeploymentsPath.Length);

            builder.Path = path;

            var baseUrl = builder.Uri.ToString().TrimEnd('/');
            var rel = (relativePath ?? string.Empty).TrimStart('/');
            return string.Concat(baseUrl, "/", rel);
        }

        private static string BuildSchemasLatestPath(string gameId)
        {
            return BuildPath(SchemasLatestPathTemplate, gameId);
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
            req.timeout = Mathf.Max(1, _settings.TimeoutSeconds);
            var op = req.SendWebRequest();

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
                var body = req.downloadHandler?.text;
                var detailText = string.IsNullOrWhiteSpace(body) ? req.error : body;
                throw new InvalidOperationException(
                    $"Request failed. HTTP {(int)req.responseCode}. Endpoint: {req.url}. Details: {detailText}");
            }
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
