using System;
using System.IO;
using System.Collections.Generic;
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
        private const string DeploymentEndpointPath = "/deployments";
        private const string SchemasLatestPathTemplate = "schemas/{0}/latest";
        private const string RemoteCodeHashPathTemplate = "games/{0}/rpc-code/hash";
        private const string LatestVersionPathTemplate = "games/{0}/version/latest";
        private const string CodeArchivePathTemplate = "games/{0}/code-archive";
        private const int MaxUploadRedirects = 3;
        private readonly PlayServConfig _settings;

        public DeploymentApiClient(PlayServConfig settings)
        {
            _settings = settings;
        }

        public async Task UploadDeploymentAsync(string gameId, string zipPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.DeployApiServerAddress))
                throw new InvalidOperationException("DeploymentSettings.DeployApiServerAddress is empty.");

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            if (!File.Exists(zipPath))
                throw new FileNotFoundException("ZIP file not found.", zipPath);
            
            var url = BuildDeployUploadUrl(_settings.DeployApiServerAddress);

            // Read ZIP bytes
            var data = File.ReadAllBytes(zipPath);
            if (data.Length == 0)
                throw new InvalidOperationException($"ZIP file is empty: {zipPath}");

            var authToken = ResolveDeployAuthToken();
            var currentUrl = url;
            var response = await SendDeploymentUploadRequestAsync(currentUrl, gameId, data, authToken, useMultipart: false, ct);

            var redirectCount = 0;
            while (IsRedirect(response.ResponseCode) &&
                   !string.IsNullOrWhiteSpace(response.RedirectLocation) &&
                   redirectCount < MaxUploadRedirects)
            {
                currentUrl = ResolveRedirectUrl(response.RedirectLocation, currentUrl);
                response = await SendDeploymentUploadRequestAsync(currentUrl, gameId, data, authToken, useMultipart: false, ct);
                redirectCount++;
            }

            if (IsRedirect(response.ResponseCode) && !string.IsNullOrWhiteSpace(response.RedirectLocation))
            {
                throw new InvalidOperationException(
                    $"Upload redirect limit reached ({MaxUploadRedirects}). Last URL: {currentUrl}. Location: {response.RedirectLocation}");
            }

            response = await RetryAsMultipartIfNeededAsync(response, currentUrl, gameId, data, authToken, redirectCount > 0, ct);

            if (!IsSuccessfulUploadStatus(response.ResponseCode))
            {
                throw new InvalidOperationException(BuildUploadErrorMessage(
                    response.ResponseCode,
                    response.ReasonPhrase,
                    response.Body,
                    currentUrl,
                    gameId));
            }
        }

        public async Task FetchLatestSchemasAsync(string gameId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            var url = BuildApiRelativeUrl(_settings.DeployApiServerAddress, BuildSchemasLatestPath(gameId));

            using var req = UnityWebRequest.Get(url);
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);
        }

        public async Task<string> GetRemoteCodeHashAsync(string gameId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            var url = BuildApiRelativeUrl(_settings.DeployApiServerAddress, BuildPath(RemoteCodeHashPathTemplate, gameId));

            using var req = UnityWebRequest.Get(url);
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);

            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Remote hash response body is empty.");

            var obj = JObject.Parse(body);
            var hash = GetJsonValueIgnoreCase(obj, "hash");

            if (string.IsNullOrWhiteSpace(hash))
                throw new InvalidOperationException("Remote hash was not found in response.");

            return hash;
        }

        public async Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            var url = BuildApiRelativeUrl(_settings.DeployApiServerAddress, BuildPath(LatestVersionPathTemplate, gameId));

            using var req = UnityWebRequest.Get(url);
            AddCommonHeaders(req);
            await SendRequestAsync(req, ct);

            var body = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Latest version response body is empty.");

            var obj = JObject.Parse(body);
            var version = GetJsonValueIgnoreCase(obj, "version");

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

            var url = BuildApiRelativeUrl(_settings.DeployApiServerAddress, BuildPath(CodeArchivePathTemplate, gameId));

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

        private static string BuildApiBaseUrl(string serverAddress)
        {
            serverAddress = NormalizeEndpoint(serverAddress);

            if (!Uri.TryCreate(serverAddress, UriKind.Absolute, out var endpointUri))
                throw new InvalidOperationException($"DeploymentSettings.DeployApiServerAddress is invalid: {serverAddress}");

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

        private static string BuildDeployUploadUrl(string serverAddress)
        {
            return BuildApiBaseUrl(serverAddress) + DeploymentEndpointPath;
        }

        private static string BuildApiRelativeUrl(string serverAddress, string relativePath)
        {
            var baseUrl = BuildApiBaseUrl(serverAddress);
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

        private static string GetJsonValueIgnoreCase(JObject obj, string key)
        {
            if (obj == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token)
                ? token?.ToString()?.Trim()
                : string.Empty;
        }

        private async Task<DeploymentUploadResponse> RetryAsMultipartIfNeededAsync(
            DeploymentUploadResponse response,
            string requestUrl,
            string gameId,
            byte[] data,
            string authToken,
            bool afterRedirect,
            CancellationToken ct)
        {
            if (IsSuccessfulUploadStatus(response.ResponseCode))
                return response;

            if (!ShouldRetryAsMultipart(response.ResponseCode, response.Body))
                return response;

            Debug.LogWarning(afterRedirect
                ? "[PlayServ] Upload rejected as empty ZIP body after redirect. Retrying as multipart/form-data."
                : "[PlayServ] Upload rejected as empty ZIP body. Retrying as multipart/form-data.");

            return await SendDeploymentUploadRequestAsync(
                requestUrl,
                gameId,
                data,
                authToken,
                useMultipart: true,
                ct: ct);
        }

        private async Task<DeploymentUploadResponse> SendDeploymentUploadRequestAsync(
            string url,
            string gameId,
            byte[] data,
            string authToken,
            bool useMultipart,
            CancellationToken ct)
        {
            using var request = useMultipart
                ? CreateUploadMultipartRequest(url, gameId, data, authToken)
                : CreateUploadRawRequest(url, gameId, data, authToken);

            request.timeout = Mathf.Max(1, _settings.TimeoutSeconds);
            request.redirectLimit = 0;

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(50, ct);
            }

            var responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            return new DeploymentUploadResponse(
                request.responseCode,
                responseBody,
                request.GetResponseHeader("Location"),
                request.error,
                request.url);
        }

        private static bool IsSuccessfulUploadStatus(long responseCode)
        {
            return responseCode is >= 200 and <= 299;
        }

        private static string ResolveRedirectUrl(string locationHeader, string requestUrl)
        {
            if (string.IsNullOrWhiteSpace(locationHeader))
                throw new InvalidOperationException($"Redirect without Location header from {requestUrl}.");

            if (Uri.TryCreate(locationHeader, UriKind.Absolute, out var redirectUri))
                return redirectUri.ToString();

            var baseUri = new Uri(requestUrl);
            if (Uri.TryCreate(baseUri, locationHeader, out redirectUri))
                return redirectUri.ToString();

            throw new InvalidOperationException($"Unable to resolve redirect target '{locationHeader}' from {requestUrl}.");
        }

        private static UnityWebRequest CreateUploadRawRequest(string url, string gameId, byte[] data, string authToken)
        {
            var request = new UnityWebRequest(url, "POST")
            {
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.uploadHandler = new UploadHandlerRaw(data)
            {
                contentType = "application/zip"
            };

            request.SetRequestHeader("X-Game-Id", gameId);
            if (!string.IsNullOrWhiteSpace(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            return request;
        }

        private static UnityWebRequest CreateUploadMultipartRequest(string url, string gameId, byte[] data, string authToken)
        {
            var sections = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", data, "deployment.zip", "application/zip")
            };

            var request = UnityWebRequest.Post(url, sections);
            request.method = UnityWebRequest.kHttpVerbPOST;
            request.SetRequestHeader("X-Game-Id", gameId);

            if (!string.IsNullOrWhiteSpace(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            return request;
        }

        private static bool ShouldRetryAsMultipart(long statusCode, string responseBody)
        {
            if (statusCode != 400)
                return false;

            if (string.IsNullOrWhiteSpace(responseBody))
                return false;

            return responseBody.IndexOf("Request body is empty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   responseBody.IndexOf("valid ZIP archive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRedirect(long statusCode)
        {
            return statusCode == 301 ||
                   statusCode == 302 ||
                   statusCode == 303 ||
                   statusCode == 307 ||
                   statusCode == 308;
        }

        private sealed class DeploymentUploadResponse
        {
            public DeploymentUploadResponse(long responseCode, string body, string redirectLocation, string error, string url)
            {
                ResponseCode = responseCode;
                Body = body;
                RedirectLocation = redirectLocation;
                Error = error;
                Url = url;
            }

            public long ResponseCode { get; }
            public string Body { get; }
            public string RedirectLocation { get; }
            public string Error { get; }
            public string Url { get; }

            public string ReasonPhrase => string.IsNullOrWhiteSpace(Error) ? $"HTTP {(int)ResponseCode}" : Error;
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
