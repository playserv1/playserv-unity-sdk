using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Playserv.Editor;
using Playserv.ModelGenerator.Editor;
using Playserv.Modules;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public static class SchemaLoader
{
    private const string SchemaBySdkKeyEndpointPath = "/api/schemas/by-sdk-key";
    private const string LatestSchemaFileName = "latest-schema.json";
    private const int SchemaRequestTimeoutSeconds = 30;
    
    public static Task<bool> LoadSchema(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "A public PlayServ Client Token is required to download the server schema.");
        }

        var normalizedToken = token.Trim();
        if (!normalizedToken.StartsWith("pk_", StringComparison.Ordinal) ||
            normalizedToken.IndexOf('\r') >= 0 ||
            normalizedToken.IndexOf('\n') >= 0)
        {
            throw new InvalidOperationException(
                "Server schema download accepts only a public pk_* Client Token.");
        }

        return DownloadSchema(normalizedToken);
    }

    public static void CheckNewSchema()
    {
        SchemaCodeGenerator.CheckNewVersionJsonSchema();
    }
    
    private static async Task<bool> DownloadSchema(string token)
    {
        var schemaUrl = ResolveSchemaUrl();
        return await DownloadAndSaveToResourcesAsync(schemaUrl, token);
    }
    
    private static async Task<bool> DownloadAndSaveToResourcesAsync(string url, string token)
    {
        var json = await LoadJson(url, token);
        if (json == null)
            return false;

        if (string.IsNullOrWhiteSpace(json))
        {
            DeleteLatestSchemaFileIfExists();
            ShowNoSchemaDataMessage();
            return false;
        }
        
        var resourcesDir = Path.Combine(Application.dataPath, "Resources");
        if (!Directory.Exists(resourcesDir))
            Directory.CreateDirectory(resourcesDir);

        var filePath = Path.Combine(resourcesDir, LatestSchemaFileName);
        await File.WriteAllTextAsync(filePath, json);
        
        AssetDatabase.Refresh();
        ResetSchemaSelectionProviderIfAvailable();

        Debug.Log($"[SchemaDownloader] schema.json saved to {filePath}");
        return true;
    }

    private static async Task<string> LoadJson(string url, string sdkKey)
    {
        Debug.Log($"[SchemaDownloader] Requesting schema from {url}");

        var payload = $"{{\"key\":\"{EscapeJsonString(sdkKey)}\"}}";
        var unityResult = await TryLoadJsonWithUnityWebRequest(url, payload);
        if (unityResult.Success)
            return unityResult.Text;

        if (!unityResult.ShouldFallback)
            return null;

        Debug.LogWarning($"[SchemaDownloader] UnityWebRequest failed: {unityResult.Error}. Retrying with HttpClient.");
        var httpResult = await TryLoadJsonWithHttpClient(url, payload);
        if (httpResult.Success)
            return httpResult.Text;

        Debug.LogWarning($"[SchemaDownloader] HttpClient failed: {httpResult.Error}. Retrying with system curl.");
        return await LoadJsonWithSystemCurl(url, payload);
    }

    private static async Task<SchemaLoadResult> TryLoadJsonWithUnityWebRequest(string url, string payload)
    {
        var bodyRaw = Encoding.UTF8.GetBytes(payload);
        
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = SchemaRequestTimeoutSeconds;
        
        request.SetRequestHeader("Accept", "application/json, text/plain");
        request.SetRequestHeader("Content-Type", "application/json");
        
        var operation = request.SendWebRequest();

        while (!operation.isDone)
            await Task.Yield();
        
        if (request.result != UnityWebRequest.Result.Success)
        {
            var error = $"{request.responseCode} {request.error}";
            var shouldFallback = request.responseCode == 0 &&
                                 request.result == UnityWebRequest.Result.ConnectionError;
            if (!shouldFallback)
                Debug.LogError($"[SchemaDownloader] Error: {error}");

            return SchemaLoadResult.Failed(error, shouldFallback);
        }
        
        return SchemaLoadResult.Loaded(request.downloadHandler.text);
    }

    private static async Task<SchemaLoadResult> TryLoadJsonWithHttpClient(string url, string payload)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(SchemaRequestTimeoutSeconds);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain");

            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return SchemaLoadResult.Failed(
                    $"{(int)response.StatusCode} {response.ReasonPhrase}. {TrimForLog(responseText)}",
                    true);
            }

            return SchemaLoadResult.Loaded(responseText);
        }
        catch (Exception e)
        {
            return SchemaLoadResult.Failed(GetExceptionMessage(e), true);
        }
    }

    private static async Task<string> LoadJsonWithSystemCurl(string url, string payload)
    {
        var curlPath = ResolveCurlPath();
        if (string.IsNullOrEmpty(curlPath))
        {
            Debug.LogError("[SchemaDownloader] System curl was not found. Schema download failed.");
            return null;
        }

        try
        {
            var arguments = BuildCurlArguments(url, payload);
            var result = await Task.Run(() => RunCurl(curlPath, arguments));
            if (!result.Success)
            {
                ReportFinalDownloadFailure(url, result.Error);
                return null;
            }

            return result.Text;
        }
        catch (Exception e)
        {
            ReportFinalDownloadFailure(url, GetExceptionMessage(e));
            return null;
        }
    }

    private static CurlResult RunCurl(string curlPath, string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = curlPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
            return CurlResult.Failed("Failed to start curl process.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((SchemaRequestTimeoutSeconds + 5) * 1000))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Best-effort cleanup only.
            }

            return CurlResult.Failed("Timed out.");
        }

        if (process.ExitCode != 0)
            return CurlResult.Failed(string.IsNullOrWhiteSpace(error) ? $"Exit code {process.ExitCode}." : error.Trim());

        const string statusMarker = "__PLAYSERV_HTTP_STATUS__:";
        var markerIndex = output.LastIndexOf(statusMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return CurlResult.Failed("Missing HTTP status marker in curl output.");

        var body = output.Substring(0, markerIndex);
        var statusText = output.Substring(markerIndex + statusMarker.Length).Trim();
        if (!int.TryParse(statusText, out var statusCode))
            return CurlResult.Failed($"Invalid HTTP status from curl: {statusText}");

        if (statusCode < 200 || statusCode >= 300)
            return CurlResult.Failed($"{statusCode}. {TrimForLog(body)}");

        return CurlResult.Loaded(body);
    }

    private static string ResolveCurlPath()
    {
        if (Application.platform == RuntimePlatform.OSXEditor && File.Exists("/usr/bin/curl"))
            return "/usr/bin/curl";

        if (Application.platform == RuntimePlatform.LinuxEditor && File.Exists("/usr/bin/curl"))
            return "/usr/bin/curl";

        return Application.platform == RuntimePlatform.WindowsEditor ? "curl.exe" : "curl";
    }

    private static string BuildCurlArguments(string url, string payload)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, "--silent");
        AppendArgument(builder, "--show-error");
        AppendArgument(builder, "--location");
        AppendArgument(builder, "--max-time");
        AppendArgument(builder, SchemaRequestTimeoutSeconds.ToString());
        AppendArgument(builder, "--request");
        AppendArgument(builder, "POST");
        AppendArgument(builder, "--header");
        AppendArgument(builder, "Accept: application/json, text/plain");
        AppendArgument(builder, "--header");
        AppendArgument(builder, "Content-Type: application/json");
        AppendArgument(builder, "--data-binary");
        AppendArgument(builder, payload);
        AppendArgument(builder, "--write-out");
        AppendArgument(builder, "__PLAYSERV_HTTP_STATUS__:%{http_code}");
        AppendArgument(builder, url);
        return builder.ToString();
    }

    private static void AppendArgument(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
            builder.Append(' ');

        builder.Append(QuoteArgument(value));
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"") + "\"";
    }

    private static string GetExceptionMessage(Exception exception)
    {
        if (exception == null)
            return string.Empty;

        return exception.InnerException == null
            ? exception.Message
            : $"{exception.Message} Inner: {exception.InnerException.Message}";
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        return value.Length <= 240 ? value : value.Substring(0, 240) + "...";
    }

    private static void ShowNoSchemaDataMessage()
    {
        const string message = "Немає даних. Schema API повернув порожній JSON, файл latest-schema.json не створено.";
        Debug.LogError($"[SchemaDownloader] {message}");
        EditorUtility.DisplayDialog("PlayServ Schema", message, "OK");
    }

    private static void ReportFinalDownloadFailure(string url, string error)
    {
        var trimmedError = TrimForLog(error);
        if (IsTlsSniError(error))
        {
            var host = GetHostForMessage(url);
            var message =
                $"Schema API TLS/SNI помилка для host '{host}'. Сервер не приймає TLS server name для цього endpoint-а.\n\n" +
                $"URL: {url}\n\n" +
                "Перевір Schema API Server у PlayServConfig / package defaults або TLS/ingress конфіг на сервері.\n\n" +
                $"curl: {trimmedError}";

            Debug.LogError($"[SchemaDownloader] {message.Replace('\n', ' ')}");
            EditorUtility.DisplayDialog("PlayServ Schema TLS", message, "OK");
            return;
        }

        Debug.LogError($"[SchemaDownloader] curl error for {url}: {trimmedError}");
    }

    private static bool IsTlsSniError(string error)
    {
        return !string.IsNullOrEmpty(error) &&
               error.IndexOf("tlsv1 unrecognized name", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetHostForMessage(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }

    private static void DeleteLatestSchemaFileIfExists()
    {
        if (!File.Exists(
                PlayServServerSchemaWorkflow.ToAbsolutePath(
                    PlayServServerSchemaWorkflow.LatestSchemaAssetPath)))
        {
            return;
        }

        AssetDatabase.DeleteAsset(
            PlayServServerSchemaWorkflow.LatestSchemaAssetPath);
        AssetDatabase.Refresh();
    }

    private static string ResolveSchemaUrl()
    {
        var config = Resources.Load<PlayServConfig>("PlayServConfig");
        if (config != null)
            return BuildSchemaUrl(
                PlayServSettingsResolver.ResolveEditorSettings(config).SchemaApiServerAddress,
                "Resources/PlayServConfig");

        if (PlayServPackageDefaultsProvider.TryLoadSettings(out var packageDefaults))
            return BuildSchemaUrl(packageDefaults.SchemaApiServerAddress, "PlayServPackageDefaults");

        throw new InvalidOperationException(
            "schemaApiServerAddress is not configured in PlayServConfig or baked package defaults.");
    }

    private static string BuildSchemaUrl(string serverAddress, string source)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
            throw new InvalidOperationException("schemaApiServerAddress is required.");

        var url = serverAddress.Trim().TrimEnd('/') + SchemaBySdkKeyEndpointPath;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"schemaApiServerAddress is not a valid HTTP URL: {serverAddress}");
        }

        Debug.Log($"[SchemaDownloader] Schema API endpoint resolved from {source}: {url}");
        return url;
    }

    private static void ResetSchemaSelectionProviderIfAvailable()
    {
        if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(PlayServModuleManifest.DataSubscriptionId))
            return;

        PlayServSchemaSelectionRegistry.TryReset();
    }

    private sealed class SchemaLoadResult
    {
        private SchemaLoadResult(bool success, string text, string error, bool shouldFallback)
        {
            Success = success;
            Text = text;
            Error = error;
            ShouldFallback = shouldFallback;
        }

        public bool Success { get; }

        public string Text { get; }

        public string Error { get; }

        public bool ShouldFallback { get; }

        public static SchemaLoadResult Loaded(string text)
        {
            return new SchemaLoadResult(true, text, string.Empty, false);
        }

        public static SchemaLoadResult Failed(string error, bool shouldFallback)
        {
            return new SchemaLoadResult(false, null, error ?? string.Empty, shouldFallback);
        }
    }

    private sealed class CurlResult
    {
        private CurlResult(bool success, string text, string error)
        {
            Success = success;
            Text = text;
            Error = error;
        }

        public bool Success { get; }

        public string Text { get; }

        public string Error { get; }

        public static CurlResult Loaded(string text)
        {
            return new CurlResult(true, text, string.Empty);
        }

        public static CurlResult Failed(string error)
        {
            return new CurlResult(false, null, error ?? string.Empty);
        }
    }
}
