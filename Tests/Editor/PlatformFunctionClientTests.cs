using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Playserv.Editor.Tests
{
    public class PlatformFunctionClientTests
    {
        private sealed class Handler : HttpMessageHandler
        {
            public readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
            public int Calls;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(Responses.Dequeue()(request)); }
            public void Add(string json, HttpStatusCode code = HttpStatusCode.OK) => Responses.Enqueue(_ => Json(json, code));
        }
        private static T AssertTaskThrows<T>(Func<Task> action) where T : Exception
        {
            return Assert.Throws<T>(() => RunTask(action));
        }

        private static void RunTask(Func<Task> action)
        {
            Task.Run(action).GetAwaiter().GetResult();
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new HttpResponseMessage(code) { Content = new StringContent(json) };
        private const string Auth = "{\"access_token\":\"session-secret\",\"project_slug\":\"eggie\",\"env\":\"dev\"}";
        private static PlatformFunctionClient Client(Handler h) => new PlatformFunctionClient("https://platform.example", "server-secret", new HttpClient(h), (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        private static PlatformDeploymentReference Ref() => new PlatformDeploymentReference { Api = "https://platform.example", Project = "eggie", Environment = "dev", Id = "deploy-1" };

        [TestCase("cloud_function")]
        [TestCase("game_server")]
        public void UploadUsesV1AuthScopedMultipartAndReturnsDeploymentId(string kind) => RunTask(async () =>
        {
            var h = new Handler();
            h.Responses.Enqueue(r => { Assert.That(r.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/auth/cli")); Assert.That(r.Headers.Authorization.Parameter, Is.EqualTo("server-secret")); return Json(Auth); });
            h.Responses.Enqueue(r =>
            {
                Assert.That(r.Method, Is.EqualTo(HttpMethod.Post)); Assert.That(r.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/platform-functions/room/deployments"));
                Assert.That(r.Headers.Authorization.Parameter, Is.EqualTo("session-secret"));
                Assert.That(r.Headers.GetValues("X-Project-Slug"), Is.EqualTo(new[] { "eggie" }));
                Assert.That(r.Headers.GetValues("X-Env"), Is.EqualTo(new[] { "dev" }));
                var body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                foreach (var text in new[] { kind, "csharp", "target_env", "dev", "application/gzip", "source" }) Assert.That(body, Does.Contain(text));
                return Json("{\"id\":\"deploy-1\"}", HttpStatusCode.Accepted);
            });
            using (var client = Client(h)) { await client.ConnectAsync(default); Assert.That((await client.UploadAsync("room", kind, new byte[] { 1, 2 }, default)).Id, Is.EqualTo("deploy-1")); }
            Assert.That(h.Calls, Is.EqualTo(2));
        });

        [Test] public void PollWaitsUntilDeployedAndRefreshesExpiredSessionOnce() => RunTask(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Add("{}", HttpStatusCode.Unauthorized); h.Add(Auth); h.Add("{\"phase\":\"building\"}"); h.Add("{\"phase\":\"deployed\"}");
            using (var c = Client(h)) { await c.ConnectAsync(default); Assert.That(await c.PollAsync(Ref(), null, default), Is.EqualTo("deployed")); }
            Assert.That(h.Calls, Is.EqualTo(5));
        });

        [Test] public void RefreshCannotSwitchTarget() => RunTask(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Add("{}", HttpStatusCode.Unauthorized); h.Add(Auth.Replace("dev", "prod"));
            using (var c = Client(h)) { await c.ConnectAsync(default); AssertTaskThrows<InvalidOperationException>(() => c.PollAsync(Ref(), null, default)); }
            Assert.That(h.Calls, Is.EqualTo(3));
        });

        [Test] public void FailedDeploymentReportsMessageWithSecretsRedacted() => RunTask(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Add("{\"phase\":\"failed\",\"message\":\"compiler server-secret session-secret\"}");
            using (var c = Client(h))
            {
                await c.ConnectAsync(default);
                var error = AssertTaskThrows<InvalidOperationException>(() => c.PollAsync(Ref(), null, default));
                Assert.That(error.Message, Does.Contain("compiler")); Assert.That(error.Message, Does.Not.Contain("server-secret")); Assert.That(error.Message, Does.Not.Contain("session-secret"));
            }
        });

        [Test] public void UncertainUploadIsNeverRetried() => RunTask(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Responses.Enqueue(_ => throw new HttpRequestException("lost response"));
            using (var c = Client(h)) { await c.ConnectAsync(default); AssertTaskThrows<HttpRequestException>(() => c.UploadAsync("room", "game_server", new byte[] { 1 }, default)); }
            Assert.That(h.Calls, Is.EqualTo(2));
        });

        [Test] public void ResumeOnlyPollsAndRejectsWrongTarget() => RunTask(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Responses.Enqueue(r => { Assert.That(r.Method, Is.EqualTo(HttpMethod.Get)); return Json("{\"phase\":\"deployed\"}"); });
            using (var c = Client(h))
            {
                await c.ConnectAsync(default); var d = Ref(); d.Environment = "prod";
                AssertTaskThrows<InvalidOperationException>(() => c.PollAsync(d, null, default));
                await c.PollAsync(Ref(), null, default);
            }
        });

        [Test] public void TimeoutAndCancellationStopPolling() => RunTask(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Add("{\"phase\":\"building\"}"); h.Add("{\"phase\":\"building\"}");
            using (var c = Client(h))
            {
                await c.ConnectAsync(default); AssertTaskThrows<TimeoutException>(() => c.PollAsync(Ref(), null, default, 2));
                var cancel = new CancellationTokenSource(); cancel.Cancel();
                Assert.That(() => RunTask(() => c.PollAsync(Ref(), null, cancel.Token)), Throws.InstanceOf<OperationCanceledException>());
            }
            Assert.That(h.Calls, Is.EqualTo(3));
        });

        [TestCase("{}")]
        [TestCase("not json")]
        public void InvalidAuthDoesNotCreateSession(string json)
        {
            var h = new Handler(); h.Add(json);
            using (var c = Client(h)) AssertTaskThrows<InvalidOperationException>(() => c.ConnectAsync(default));
        }

        [Test] public void TargetMismatchDuringRefreshInvalidatesSessionForSubsequentUpload() => RunTask(async () =>
        {
            var h = new Handler(); h.Add(Auth); h.Add("{}", HttpStatusCode.Unauthorized); h.Add(Auth.Replace("dev", "prod")); h.Add("{\"id\":\"bad\"}");
            using (var c = Client(h))
            {
                await c.ConnectAsync(default);
                AssertTaskThrows<InvalidOperationException>(() => c.PollAsync(Ref(), null, default));
                AssertTaskThrows<InvalidOperationException>(() => c.UploadAsync("room", "game_server", new byte[] { 1 }, default));
            }
            Assert.That(h.Calls, Is.EqualTo(3));
        });

        private sealed class BlockingHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.Method == HttpMethod.Post) return Json(Auth);
                await Task.Delay(300, ct);
                return Json("{\"phase\":\"deployed\"}");
            }
        }
        [Test] public void PollDeadlineIncludesInflightRequestTime()
        {
            Assert.Throws<TimeoutException>(() => RunTask(async () =>
            {
                using (var c = new PlatformFunctionClient("https://platform.example", "key", new HttpClient(new BlockingHandler()), pollTimeout: TimeSpan.FromMilliseconds(40)))
                {
                    await c.ConnectAsync(default);
                    await c.PollAsync(Ref(), null, default);
                }
            }));
        }

        [TestCase("https://platform.example/api")]
        [TestCase("https://user:secret@platform.example")]
        [TestCase("http://platform.example")]
        public void UnsafeApiOriginsAreRejected(string api) => Assert.Throws<InvalidOperationException>(() => new PlatformFunctionClient(api, "key"));
    }
}
