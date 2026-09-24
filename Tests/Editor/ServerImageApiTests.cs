using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Playserv.Editor.Tests
{
    public class ServerImageApiTests
    {
        private const string Auth = "{\"access_token\":\"operator-secret\",\"project_slug\":\"tanks\",\"env\":\"dev\"}";
        private sealed class Handler : HttpMessageHandler
        {
            internal readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> Replies = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
            internal int Calls;
            internal void Add(string json, HttpStatusCode status = HttpStatusCode.OK) => Replies.Enqueue(_ => Json(json, status));
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(Replies.Dequeue()(r)); }
        }
        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new HttpResponseMessage(status) { Content = new StringContent(json) };
        private static PlatformFunctionClient Client(Handler h) => new PlatformFunctionClient("https://platform.example", "sk_secret", new HttpClient(h));
        private static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
        private static void Scope(HttpRequestMessage r)
        {
            Assert.That(r.Headers.Authorization.Parameter, Is.EqualTo("operator-secret"));
            Assert.That(r.Headers.GetValues("X-Project-Slug"), Is.EqualTo(new[] { "tanks" }));
            Assert.That(r.Headers.GetValues("X-Env"), Is.EqualTo(new[] { "dev" }));
        }
        [Test] public void ListsOnlyGameServersAcrossAllPages() => Run(async () =>
        {
            var h = new Handler(); h.Add(Auth);
            h.Replies.Enqueue(r => { Scope(r); Assert.That(r.RequestUri.PathAndQuery, Is.EqualTo("/api/v1/functions?kind=game_server&limit=100")); return Json("{\"data\":[{\"slug\":\"room\",\"kind\":\"game_server\"},{\"slug\":\"function\",\"kind\":\"cloud_function\"}],\"page\":{\"has_more\":true,\"cursor_next\":\"a/b+\"}}"); });
            h.Replies.Enqueue(r => { Scope(r); Assert.That(r.RequestUri.Query, Does.Contain("cursor=a%2Fb%2B")); return Json("{\"data\":[{\"slug\":\"second\",\"kind\":\"game_server\"}],\"page\":{\"has_more\":false,\"cursor_next\":null}}"); });
            using (var c = Client(h)) { await c.ConnectAsync(default); Assert.That(await c.ListGameServersAsync(default), Is.EqualTo(new[] { "room", "second" })); }
        });
        [Test] public void CredentialsUseScopedPostAndSecretsAreRedacted() => Run(async () =>
        {
            var h = new Handler(); h.Add(Auth);
            h.Replies.Enqueue(r => { Scope(r); Assert.That(r.Method, Is.EqualTo(HttpMethod.Post)); Assert.That(r.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/server-images:credentials")); Assert.That(r.Headers.Contains("Idempotency-Key"), Is.False); return Json("{\"secret\":\"registry-secret\"}", HttpStatusCode.Created); });
            using (var c = Client(h)) { await c.ConnectAsync(default); await c.IssueImageCredentialsAsync(default); Assert.That(c.Redact("sk_secret operator-secret registry-secret"), Is.EqualTo("[redacted] [redacted] [redacted]")); }
        });
        [Test] public void RefreshBeforePublishCannotChangeScope() => Run(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Add(Auth.Replace("dev", "prod"));
            using (var c = Client(h)) { await c.ConnectAsync(default); Assert.Throws<InvalidOperationException>(() => Run(() => c.RefreshImageSessionAsync(default))); Assert.Throws<InvalidOperationException>(() => Run(() => c.IssueImageCredentialsAsync(default))); }
            Assert.That(h.Calls, Is.EqualTo(2));
        });
        [Test] public void ExpiredReadSessionIsRenewedOnceWithSameScope() => Run(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Add("{}", HttpStatusCode.Unauthorized); h.Add(Auth);
            h.Replies.Enqueue(r => { Scope(r); Assert.That(r.Method, Is.EqualTo(HttpMethod.Get)); return Json("{\"images\":[]}"); });
            using (var c = Client(h)) { await c.ConnectAsync(default); Assert.That((await c.ListServerImagesAsync(default))["images"], Is.Empty); }
            Assert.That(h.Calls, Is.EqualTo(4));
        });
        [Test] public void LostCredentialResponseIsNotRetried() => Run(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Replies.Enqueue(_ => throw new HttpRequestException("lost response"));
            using (var c = Client(h)) { await c.ConnectAsync(default); Assert.Throws<HttpRequestException>(() => Run(() => c.IssueImageCredentialsAsync(default))); }
            Assert.That(h.Calls, Is.EqualTo(2));
        });
        [Test] public void RetryAfterIsAvailableToPublicationPolling() => Run(async () =>
        {
            var h = new Handler(); h.Add(Auth);
            h.Replies.Enqueue(_ => { var r = Json("{\"code\":\"busy\"}", (HttpStatusCode)429); r.Headers.TryAddWithoutValidation("Retry-After", "7"); return r; });
            using (var c = Client(h)) { await c.ConnectAsync(default); var e = Assert.Throws<ServerImageRequestException>(() => Run(() => c.ListServerImagesAsync(default))); Assert.That(e.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(7))); }
        });
    }
}
