using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PlayServ.Schema.Tool;

internal sealed class SchemaPushClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    public SchemaPushClient(TimeSpan timeout)
    {
        _http = new HttpClient { Timeout = timeout };
        _jsonOptions = ProjectConfiguration.CreateJsonOptions();
        _jsonOptions.PropertyNamingPolicy = null;
        _jsonOptions.WriteIndented = false;
    }

    public SchemaPushOutcome Push(
        string endpoint,
        string serverKey,
        string expectedProjectId,
        string expectedEnvironment,
        IReadOnlyList<SchemaContract> contracts)
    {
        endpoint = NormalizeEndpoint(endpoint);
        serverKey = NormalizeServerKey(serverKey);

        using var authRequest = new HttpRequestMessage(HttpMethod.Post, endpoint + "/api/v1/auth/cli");
        authRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serverKey);
        using var authResponse = _http.SendAsync(authRequest).GetAwaiter().GetResult();
        var authBody = authResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        EnsureSuccess(authResponse, authBody, "CLI credential exchange");
        using var authJson = JsonDocument.Parse(authBody);
        var authRoot = authJson.RootElement;
        var accessToken = RequireString(authRoot, "access_token", "CLI credential exchange");
        var projectId = RequireString(authRoot, "project_id", "CLI credential exchange");
        var projectSlug = RequireString(authRoot, "project_slug", "CLI credential exchange");
        var environment = RequireString(authRoot, "env", "CLI credential exchange");
        if (!string.IsNullOrWhiteSpace(expectedProjectId) &&
            !string.Equals(expectedProjectId.Trim(), projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The exchanged server key belongs to a different PlayServ project.");
        }
        if (!string.IsNullOrWhiteSpace(expectedEnvironment) &&
            !string.Equals(expectedEnvironment.Trim(), environment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The exchanged server key belongs to a different PlayServ environment.");
        }

        using var overviewRequest = CreateOperatorRequest(
            HttpMethod.Get,
            endpoint + "/api/v1/schema",
            accessToken,
            projectSlug,
            environment);
        using var overviewResponse = _http.SendAsync(overviewRequest).GetAwaiter().GetResult();
        var overviewBody = overviewResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        EnsureSuccess(overviewResponse, overviewBody, "schema revision preflight");
        using var overviewJson = JsonDocument.Parse(overviewBody);
        var previousRevision = RequireString(
            overviewJson.RootElement,
            "revision",
            "schema revision preflight");

        var payload = SchemaPushPayloadBuilder.Build(contracts, previousRevision);
        using var pushRequest = CreateOperatorRequest(
            HttpMethod.Post,
            endpoint + "/api/v1/schema:push-from-code",
            accessToken,
            projectSlug,
            environment);
        pushRequest.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        pushRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, _jsonOptions),
            Encoding.UTF8,
            "application/json");
        using var pushResponse = _http.SendAsync(pushRequest).GetAwaiter().GetResult();
        var pushBody = pushResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        EnsureSuccess(pushResponse, pushBody, "schema push");
        using var pushJson = JsonDocument.Parse(pushBody);
        var revision = RequireString(pushJson.RootElement, "revision", "schema push");
        return new SchemaPushOutcome(previousRevision, revision, contracts.Count);
    }

    public void Dispose() => _http.Dispose();

    private static HttpRequestMessage CreateOperatorRequest(
        HttpMethod method,
        string url,
        string accessToken,
        string projectSlug,
        string environment)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("X-Project-Slug", projectSlug);
        request.Headers.TryAddWithoutValidation("X-Env", environment);
        return request;
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Configure service.endpoint or PLAYSERV_API_URL with an absolute HTTP(S) URL before pushing schemas.");
        }
        return endpoint.Trim().TrimEnd('/');
    }

    private static string NormalizeServerKey(string serverKey)
    {
        if (string.IsNullOrWhiteSpace(serverKey) ||
            !serverKey.Trim().StartsWith("sk_", StringComparison.Ordinal) ||
            serverKey.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            throw new InvalidOperationException(
                "The configured schema-push credential is missing or is not a valid sk_* server key.");
        }
        return serverKey.Trim();
    }

    private static string RequireString(JsonElement root, string property, string operation)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"{operation} returned no '{property}'.");
        }
        return value.GetString()!;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;

        var code = string.Empty;
        var detail = string.Empty;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("code", out var codeValue))
                code = codeValue.GetString() ?? string.Empty;
            if (json.RootElement.TryGetProperty("detail", out var detailValue))
                detail = detailValue.GetString() ?? string.Empty;
        }
        catch
        {
        }

        var suffix = string.IsNullOrWhiteSpace(code) ? string.Empty : " " + code;
        var explanation = string.IsNullOrWhiteSpace(detail) ? string.Empty : ": " + detail;
        throw new InvalidOperationException(
            $"{operation} was refused (HTTP {(int)response.StatusCode}{suffix}){explanation}");
    }
}

internal sealed class SchemaPushOutcome
{
    public SchemaPushOutcome(string previousRevision, string revision, int schemaCount)
    {
        PreviousRevision = previousRevision;
        Revision = revision;
        SchemaCount = schemaCount;
    }

    public string PreviousRevision { get; }
    public string Revision { get; }
    public int SchemaCount { get; }
}
