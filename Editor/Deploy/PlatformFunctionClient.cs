using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Playserv.Editor
{
    internal sealed class PlatformFunctionSession
    {
        public string Project { get; internal set; }
        public string Environment { get; internal set; }
        internal string Token;
    }
    internal sealed class PlatformDeploymentReference
    {
        public string Api, Project, Environment, Id;
    }
    internal sealed partial class PlatformFunctionClient : IDisposable
    {
        private readonly string _api, _key, _expectedEnvironment;
        private readonly HttpClient _http;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly TimeSpan _pollTimeout;
        private readonly System.Collections.Generic.HashSet<string> _secrets = new System.Collections.Generic.HashSet<string>();
        private PlatformFunctionSession _session;
        public PlatformFunctionClient(string api, string key, HttpClient http = null, Func<TimeSpan, CancellationToken, Task> delay = null, TimeSpan? pollTimeout = null, string expectedEnvironment = null)
        {
            _api = NormalizeApi(api);
            _expectedEnvironment = expectedEnvironment;
            _key = (key ?? "").Trim();
            if (_key.Length == 0 || _key.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new InvalidOperationException("A server API key is required.");
            _secrets.Add(_key);
            _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            _http.Timeout = TimeSpan.FromMinutes(5);
            _delay = delay ?? Task.Delay;
            _pollTimeout = pollTimeout ?? TimeSpan.FromMinutes(20);
            if (_pollTimeout <= TimeSpan.Zero || _pollTimeout > TimeSpan.FromMinutes(20)) throw new ArgumentOutOfRangeException(nameof(pollTimeout));
        }

        internal static string NormalizeApi(string value)
        {
            if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
                throw new InvalidOperationException("Use a Platform API HTTPS origin (HTTP is allowed for localhost). Do not include an API path.");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        public async Task<PlatformFunctionSession> ConnectAsync(CancellationToken ct)
        {
            _session = null;
            using (var request = new HttpRequestMessage(HttpMethod.Post, _api + "/api/v1/auth/cli"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
                using (var response = await _http.SendAsync(request, ct))
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Platform authentication failed (HTTP " + (int)response.StatusCode + "). Check the server key and API origin.");
                    var json = Parse(await response.Content.ReadAsStringAsync());
                    var token = Required(json, "access_token");
                    _secrets.Add(token);
                    var refresh = (string)json["refresh_token"];
                    if (!string.IsNullOrEmpty(refresh)) _secrets.Add(refresh);
                    var session = new PlatformFunctionSession { Token = token, Project = Required(json, "project_slug"), Environment = Required(json, "env") };
                    ct.ThrowIfCancellationRequested();
                    if (_expectedEnvironment != null && !string.Equals(session.Environment, _expectedEnvironment, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The Server Token belongs to a different environment. Select the matching Dev/Prod configuration and connect again.");
                    _session = session;
                    return session;
                }
            }
        }

        public async Task<PlatformDeploymentReference> UploadAsync(string slug, string kind, byte[] archive, CancellationToken ct)
        {
            EnsureSession();
            if (!Regex.IsMatch(slug ?? "", "^[a-z0-9][a-z0-9-]*$")) throw new InvalidOperationException("Function slug must contain lowercase letters, digits and hyphens.");
            if (kind != "cloud_function" && kind != "game_server") throw new InvalidOperationException("Select cloud_function or game_server.");
            if (archive == null || archive.Length == 0) throw new InvalidOperationException("Source archive is empty.");
            using (var request = ScopedRequest(HttpMethod.Post, "/api/v1/platform-functions/" + Uri.EscapeDataString(slug) + "/deployments"))
            {
                var form = new MultipartFormDataContent(); request.Content = form;
                form.Add(new StringContent(kind), "kind"); form.Add(new StringContent("csharp"), "language");
                form.Add(new StringContent(_session.Environment), "target_env");
                var source = new ByteArrayContent(archive); source.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
                form.Add(source, "source", "source.tar.gz");
                using (var response = await _http.SendAsync(request, ct))
                {
                    var json = await ReadResponse(response);
                    return new PlatformDeploymentReference { Api = _api, Project = _session.Project, Environment = _session.Environment, Id = Required(json, "id") };
                }
            }
        }

        public async Task<string> PollAsync(PlatformDeploymentReference deployment, Action<string> report, CancellationToken ct, int attempts = 200)
        {
            ValidateTarget(deployment);
            if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));
            var watch = Stopwatch.StartNew();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(_pollTimeout);
                try
                {
                    for (var attempt = 0; attempt < attempts && watch.Elapsed < _pollTimeout; attempt++)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        JObject status;
                        using (var request = ScopedRequest(HttpMethod.Get, "/api/v1/deployments/" + Uri.EscapeDataString(deployment.Id)))
                        using (var response = await _http.SendAsync(request, deadline.Token))
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                await ConnectAsync(deadline.Token);
                                try { ValidateTarget(deployment); }
                                catch { _session = null; throw; }
                                using (var retry = ScopedRequest(HttpMethod.Get, "/api/v1/deployments/" + Uri.EscapeDataString(deployment.Id)))
                                using (var refreshed = await _http.SendAsync(retry, deadline.Token)) status = await ReadResponse(refreshed);
                            }
                            else status = await ReadResponse(response);
                        }
                        var phase = Required(status, "phase"); report?.Invoke(Redact(phase));
                        if (phase == "deployed") return phase;
                        if (phase == "failed") throw new InvalidOperationException("Deployment failed: " + Redact((string)status["message"] ?? "See server deployment logs."));
                        if (attempt + 1 < attempts) await _delay(TimeSpan.FromSeconds(6), deadline.Token);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new TimeoutException("Deployment polling timed out. Resume status to continue checking the accepted deployment."); }
            }
            throw new TimeoutException("Deployment polling timed out. Resume status to continue checking the accepted deployment.");
        }

        private void EnsureSession() { if (_session == null) throw new InvalidOperationException("Connect to the Platform API before deploying."); }
        private void ValidateTarget(PlatformDeploymentReference d)
        {
            EnsureSession();
            if (d == null || string.IsNullOrWhiteSpace(d.Id) || d.Api != _api || d.Project != _session.Project || d.Environment != _session.Environment)
                throw new InvalidOperationException("The deployment belongs to a different API, project or environment. Reconnect to its original target to resume.");
        }
        private HttpRequestMessage ScopedRequest(HttpMethod method, string path)
        {
            EnsureSession(); var r = new HttpRequestMessage(method, _api + path);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
            r.Headers.Add("X-Project-Slug", _session.Project); r.Headers.Add("X-Env", _session.Environment); return r;
        }
        private async Task<JObject> ReadResponse(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                string detail = "";
                try { var error = JObject.Parse(body); detail = (string)error["detail"] ?? (string)error["message"] ?? ""; } catch (JsonException) { }
                throw new InvalidOperationException("Platform request failed (HTTP " + (int)response.StatusCode + "). " + Redact(detail));
            }
            return Parse(body);
        }
        private static JObject Parse(string body)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None })
                {
                    var json = JObject.Load(reader);
                    while (reader.Read()) { }
                    return json;
                }
            }
            catch (JsonException) { throw new InvalidOperationException("Platform API returned an invalid JSON response."); }
        }
        private static string Required(JObject json, string key)
        {
            var token = json[key];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                throw new InvalidOperationException("Platform API response is missing " + key + ".");
            return (string)token;
        }
        public string Redact(string message)
        {
            var value = message ?? "";
            foreach (var secret in _secrets) value = value.Replace(secret, "[redacted]");
            return value;
        }
        public void Dispose() => _http.Dispose();
    }
}
