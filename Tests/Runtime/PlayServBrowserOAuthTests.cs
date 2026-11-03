using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServBrowserOAuthTests
    {
        private static IEnumerator Run(Func<Task> check)
        {
            var task = check();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Platform_flow_uses_PKCE_and_polls_pending_without_sending_current_credentials() => Run(async () =>
        {
            var http = new Http();
            http.Replies.Enqueue(Response(200, "{\"authorize_url\":\"https://provider.example/login\",\"state\":\"state-secret\"}"));
            http.Replies.Enqueue(Response(401, "{}"));
            http.Replies.Enqueue(Response(200, BundleJson));
            var browser = new Browser();
            var delays = new List<TimeSpan>();
            var bundle = await PlayServBrowserOAuth.ClaimAsync("google", "pk_test", http, browser,
                new PlayServBrowserLoginOptions(), CancellationToken.None,
                (delay, ct) => { delays.Add(delay); return Task.CompletedTask; });
            Assert.That(bundle.player_id, Is.EqualTo("player-2"));
            Assert.That(browser.Url, Is.EqualTo("https://provider.example/login"));
            var json = new NewtonsoftJsonCodec();
            var start = json.Deserialize<Dictionary<string, string>>(http.Requests[0].JsonBody);
            var claim = json.Deserialize<Dictionary<string, string>>(http.Requests[1].JsonBody);
            Assert.That(start["completion"], Is.EqualTo("platform"));
            Assert.That(start["code_challenge_method"], Is.EqualTo("S256"));
            using (var hash = SHA256.Create())
                Assert.That(start["code_challenge"], Is.EqualTo(Convert.ToBase64String(hash.ComputeHash(Encoding.ASCII.GetBytes(claim["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_')));
            Assert.That(claim["state"], Is.EqualTo("state-secret"));
            Assert.That(claim["code_verifier"].Length, Is.InRange(43, 128));
            Assert.That(http.Requests[1].RequiresClientToken, Is.False);
            Assert.That(http.Requests[1].BearerToken, Is.Empty);
            Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(2) }));
        });

        [UnityTest]
        public IEnumerator Claim_network_loss_has_unknown_outcome_and_is_not_retried() => Run(async () =>
        {
            var http = new Http();
            http.Replies.Enqueue(Response(200, "{\"authorize_url\":\"https://provider.example/login\",\"state\":\"s\"}"));
            http.Failure = new InvalidOperationException("private token must not be exposed");
            var error = await Catch(() => PlayServBrowserOAuth.ClaimAsync("google", "pk_test", http, new Browser(), new PlayServBrowserLoginOptions(), CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo(PlayServAuthErrorCode.BrowserClaimOutcomeUnknown));
            Assert.That(error.ToString(), Does.Not.Contain("private token"));
            Assert.That(http.Requests.Count, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator Claim_401_remains_ambiguous_until_timeout_and_honors_retry_after() => Run(async () =>
        {
            var http = new Http();
            http.Replies.Enqueue(Response(200, "{\"authorize_url\":\"https://provider.example/login\",\"state\":\"s\"}"));
            http.Replies.Enqueue(new PlayServRuntimeDataResponse(401, "{}", null, null, headers: new Dictionary<string,string>{{"Retry-After","5"}}));
            TimeSpan observed = default;
            var error = await Catch(() => PlayServBrowserOAuth.ClaimAsync("google", "pk_test", http, new Browser(),
                new PlayServBrowserLoginOptions { Timeout = TimeSpan.FromMilliseconds(80) }, CancellationToken.None,
                (delay, ct) => { observed = delay; return Task.Delay(1000, ct); }));
            Assert.That(observed, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(error.Code, Is.EqualTo(PlayServAuthErrorCode.Timeout));
            Assert.That(error.Message, Does.Not.Contain("expired"));
        });

        internal static async Task<PlayServBrowserAuthException> Catch(Func<Task> action)
        {
            try { await action(); Assert.Fail("Expected browser auth error"); }
            catch (PlayServBrowserAuthException error) { return error; }
            return null;
        }
        internal const string BundleJson = "{\"player_id\":\"player-2\",\"access_token\":\"access-new\",\"refresh_token\":\"refresh-new\",\"expires_at\":\"2030-01-01T00:00:00Z\",\"is_new\":false}";
        internal static PlayServRuntimeDataResponse Response(int code, string json) => new PlayServRuntimeDataResponse(code, json, null, null);
        internal sealed class Browser : IPlayServOAuthBrowser
        {
            public string Url;
            public void Navigate(string url) { Url = url; }
            public void Dispose() { }
        }
        internal sealed class Http : IPlayServRuntimeHttpClient
        {
            public readonly Queue<PlayServRuntimeDataResponse> Replies = new Queue<PlayServRuntimeDataResponse>();
            public readonly List<PlayServRuntimeDataRequest> Requests = new List<PlayServRuntimeDataRequest>();
            public Exception Failure;
            public Func<PlayServRuntimeDataRequest, Task<PlayServRuntimeDataResponse>> Send;
            public Task<PlayServRuntimeDataResponse> SendDataAsync(PlayServRuntimeDataRequest request, CancellationToken ct = default)
            {
                Requests.Add(request);
                if (Send != null) return Send(request);
                if (Replies.Count != 0) return Task.FromResult(Replies.Dequeue());
                return Task.FromException<PlayServRuntimeDataResponse>(Failure ?? new Exception("No fixture reply"));
            }
            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> SignInAnonAsync(string token, CancellationToken ct = default) => Task.FromResult(new PlayerTokenBundleDto { player_id = "player-1", access_token = "old-access", refresh_token = "old-refresh", issued_at = DateTimeOffset.UtcNow.ToString("O"), expires_in = 3600 });
            public Task<PlayerRefreshResponseDto> RefreshAsync(string token, string refresh, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> LoginExternalAsync(string token, PlayerExternalLoginRequestDto request, string access = null, CancellationToken ct = default) => throw new NotSupportedException();
            public readonly List<string> Revoked = new List<string>();
            public Task SignOutAsync(string token, string refresh, CancellationToken ct = default) { Revoked.Add(refresh); return Task.CompletedTask; }
        }
    }
}
