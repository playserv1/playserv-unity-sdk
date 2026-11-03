using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Playserv.Editor
{
    internal sealed partial class PlatformFunctionClient
    {
        internal string ImageTarget { get { EnsureSession(); return _api + "|" + _session.Project + "|" + _session.Environment; } }

        internal async Task RefreshImageSessionAsync(CancellationToken ct)
        {
            EnsureSession();
            var project = _session.Project;
            var environment = _session.Environment;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var next = await ConnectAsync(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (next.Project != project || next.Environment != environment)
                {
                    _session = null;
                    throw new InvalidOperationException("The operator session changed project or environment. Reconnect and review the target.");
                }
            }
        }

        internal async Task<string[]> ListGameServersAsync(CancellationToken ct)
        {
            var slugs = new SortedSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string cursor = null;
            do
            {
                var json = await ImageRequestAsync(HttpMethod.Get, "/api/v1/functions?kind=game_server&limit=100" +
                    (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), ct);
                if (!(json["data"] is JArray rows) || !(json["page"] is JObject page))
                    throw new InvalidOperationException("Invalid game-server listing response.");
                foreach (var row in rows)
                {
                    if ((string)row["kind"] == "game_server" && !string.IsNullOrWhiteSpace((string)row["slug"]))
                        slugs.Add((string)row["slug"]);
                }
                cursor = (bool?)page["has_more"] == true ? Required(page, "cursor_next") : null;
                if (cursor != null && !cursors.Add(cursor)) throw new InvalidOperationException("Game-server listing repeated its pagination cursor.");
            } while (cursor != null);
            var result = new string[slugs.Count]; slugs.CopyTo(result); return result;
        }

        internal Task<JObject> ListServerImagesAsync(CancellationToken ct) => ImageRequestAsync(HttpMethod.Get, "/api/v1/server-images", ct);
        internal async Task<string> CreateGameServerAsync(string name, string slug, CancellationToken ct)
        {
            name = (name ?? "").Trim(); slug = (slug ?? "").Trim();
            if (name.Length == 0) throw new InvalidOperationException("Enter a game server name.");
            if (!Regex.IsMatch(slug, "^[a-z][a-z0-9-]{1,48}[a-z0-9]$") || slug.Contains("--"))
                throw new InvalidOperationException("Slug must be 3–50 lowercase letters, digits or single hyphens, starting with a letter and ending with a letter or digit.");
            ct.ThrowIfCancellationRequested();
            await RefreshImageSessionAsync(ct);
            var body = new JObject { ["name"] = name, ["slug"] = slug, ["kind"] = "game_server", ["runtime"] = "dotnet10", ["hosting_mode"] = "multi-room" };
            var result = await ImageRequestAsync(HttpMethod.Post, "/api/v1/functions", ct, body);
            ct.ThrowIfCancellationRequested();
            if ((string)result["slug"] != slug || (string)result["kind"] != "game_server")
                throw new InvalidOperationException("Unexpected creation response. Use Connect / refresh servers to check the registration before trying again.");
            return slug;
        }
        internal async Task<JObject> IssueImageCredentialsAsync(CancellationToken ct)
        {
            var json = await ImageRequestAsync(HttpMethod.Post, "/api/v1/server-images:credentials", ct);
            _secrets.Add(Required(json, "secret"));
            return json;
        }

        private async Task<JObject> ImageRequestAsync(HttpMethod method, string path, CancellationToken ct, JObject body = null)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                for (var attempt = 0; ; attempt++)
                {
                    using (var request = ScopedRequest(method, path))
                    {
                        if (body != null) request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                        using (var response = await _http.SendAsync(request, deadline.Token))
                        {
                            deadline.Token.ThrowIfCancellationRequested();
                            if (method == HttpMethod.Get && attempt == 0 && response.StatusCode == HttpStatusCode.Unauthorized)
                            { await RefreshImageSessionAsync(deadline.Token); continue; }
                            if (!response.IsSuccessStatusCode)
                            {
                                var retry = response.Headers.RetryAfter;
                                var after = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
                                string message;
                                try { await ReadResponse(response); message = "Platform request failed."; }
                                catch (InvalidOperationException error) { message = error.Message; }
                                throw new ServerImageRequestException(message, (int)response.StatusCode, after < TimeSpan.Zero ? TimeSpan.Zero : after);
                            }
                            return await ReadResponse(response);
                        }
                    }
                }
            }
        }
    }

    internal sealed class ServerImageRequestException : InvalidOperationException
    {
        internal TimeSpan? RetryAfter { get; }
        internal int Status { get; }
        internal ServerImageRequestException(string message, int status, TimeSpan? retryAfter) : base(message)
        { Status = status; RetryAfter = retryAfter; }
    }
}
