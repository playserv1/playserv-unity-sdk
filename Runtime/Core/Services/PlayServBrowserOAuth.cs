using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Proxy.Common;
using Playserv.Serialization;

namespace Playserv.Wrapper
{
    internal interface IPlayServOAuthBrowser : IDisposable { void Navigate(string url); }
    internal sealed class PlayServBrowserAuthException : Exception
    {
        internal PlayServBrowserAuthException(PlayServAuthErrorCode code, string message) : base(message) { Code = code; }
        internal PlayServAuthErrorCode Code { get; }
    }
    internal static class PlayServBrowserOAuth
    {
        internal static async Task<PlayerTokenBundleDto> ClaimAsync(string provider, string clientToken,
            IPlayServRuntimeHttpClient http, IPlayServOAuthBrowser browser, PlayServBrowserLoginOptions options,
            CancellationToken ct, Func<TimeSpan,CancellationToken,Task> delay = null,
            Func<PlayerTokenBundleDto,Task> discard = null)
        {
            var json = new NewtonsoftJsonCodec();
            var verifier = Base64Url(PlayServOAuthBrowser.RandomBytes());
            string challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            using var budget = new AsyncOperationBudget(options.Timeout, ct);
            var path = "/auth/players/" + Uri.EscapeDataString(provider);
            try
            {
                budget.Check();
                var start = await budget.RunAsync(SendAsync(http, new PlayServRuntimeDataRequest
                {
                    Method = "POST", RelativePath = path + ":start", ClientToken = clientToken,
                    JsonBody = json.Serialize(new Dictionary<string,object>
                    {
                        ["completion"] = "platform", ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
                    })
                }, budget.Token));
                EnsureSuccess(start);
                Dictionary<string,string> started;
                try { started = json.Deserialize<Dictionary<string,string>>(start.Body); }
                catch { throw Error(PlayServAuthErrorCode.InvalidResponse, "The browser sign-in start response was invalid."); }
                if (started == null || !started.TryGetValue("state", out var state) || string.IsNullOrWhiteSpace(state) ||
                    !started.TryGetValue("authorize_url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || !string.IsNullOrEmpty(uri.UserInfo))
                    throw Error(PlayServAuthErrorCode.InvalidResponse, "The browser sign-in start response was invalid.");
                budget.Check();
                browser.Navigate(url);
                while (true)
                {
                    budget.Check();
                    PlayServRuntimeDataResponse response;
                    try
                    {
                        response = await budget.RunAsync(SendAsync(http, new PlayServRuntimeDataRequest
                        {
                            Method = "POST", RelativePath = path + ":claim", RequiresClientToken = false,
                            JsonBody = json.Serialize(new Dictionary<string,object> { ["state"] = state, ["code_verifier"] = verifier })
                        }, budget.Token), async late =>
                        {
                            if (late.StatusCode == 200 && discard != null) await discard(ParseClaim(late.Body, json));
                        });
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch { throw Error(PlayServAuthErrorCode.BrowserClaimOutcomeUnknown, "The claim response was lost. Sign-in may have completed; start another login only by explicit user action."); }
                    if (response.StatusCode == 401 || response.StatusCode == 429)
                    {
                        await budget.RunAsync((delay ?? AsyncOperationBudget.Delay)(
                            AsyncOperationBudget.RetryDelay(response.Headers, TimeSpan.FromSeconds(2)), budget.Token));
                        continue;
                    }
                    EnsureSuccess(response);
                    try
                    {
                        return ParseClaim(response.Body, json);
                    }
                    catch { throw Error(PlayServAuthErrorCode.BrowserClaimOutcomeUnknown, "The claim returned an unusable session. Its outcome cannot be determined."); }
                }
            }
            catch (TimeoutException) { throw Error(PlayServAuthErrorCode.Timeout, "Browser sign-in did not complete within its time budget."); }
            finally { verifier = null; }
        }
        private static PlayerTokenBundleDto ParseClaim(string body, IJsonCodec json)
        {
            var fields = json.Deserialize<Dictionary<string,object>>(body, new JsonCodecOptions { ParseDates = false });
            string Field(string name) => fields != null && fields.TryGetValue(name, out var value) ? value as string : null;
            var player = Field("player_id"); var access = Field("access_token"); var refresh = Field("refresh_token");
            if (string.IsNullOrWhiteSpace(player) || string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) ||
                !DateTimeOffset.TryParse(Field("expires_at"), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var expiry)) throw new InvalidOperationException();
            var now = DateTimeOffset.UtcNow;
            return new PlayerTokenBundleDto
            {
                player_id = player, access_token = access, refresh_token = refresh,
                issued_at = now.ToString("O"), expires_in = (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling((expiry - now).TotalSeconds))),
                refresh_expires_in = fields.TryGetValue("refresh_expires_in", out var remaining) ? Convert.ToInt32(remaining) : 0
            };
        }
        private static async Task<PlayServRuntimeDataResponse> SendAsync(IPlayServRuntimeHttpClient http, PlayServRuntimeDataRequest request, CancellationToken ct)
        {
            try { return await http.SendDataAsync(request, ct); }
            catch (PlayServRuntimeHttpException error) when (!error.IsNetworkError && error.StatusCode > 0)
            { return new PlayServRuntimeDataResponse(error.StatusCode, error.ResponseBody, null, null, headers: error.ResponseHeaders); }
        }
        private static void EnsureSuccess(PlayServRuntimeDataResponse response)
        {
            if (response.StatusCode == 200) return;
            var code = response.StatusCode == 403 ? PlayServAuthErrorCode.Forbidden :
                response.StatusCode >= 500 ? PlayServAuthErrorCode.ServerError : PlayServAuthErrorCode.InvalidCredential;
            throw Error(code, "The platform refused browser sign-in (HTTP " + response.StatusCode + ").");
        }
        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        private static PlayServBrowserAuthException Error(PlayServAuthErrorCode code, string message) => new PlayServBrowserAuthException(code, message);
    }
}
