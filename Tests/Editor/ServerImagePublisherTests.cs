using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Playserv.Editor.Tests
{
    public class ServerImagePublisherTests
    {
        private const string Id = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Digest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string Auth = "{\"access_token\":\"operator-secret\",\"project_slug\":\"tanks\",\"env\":\"dev\"}";
        private string _folder;
        [SetUp] public void Setup() { _folder = Path.Combine(Path.GetTempPath(), "psv-image-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_folder); File.WriteAllText(Path.Combine(_folder, "Dockerfile"), "FROM scratch\n"); }
        [TearDown] public void Cleanup() => Directory.Delete(_folder, true);
        private static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
        private sealed class Http : HttpMessageHandler
        {
            internal readonly List<string> Paths = new List<string>();
            internal string ExistingDigest, Architecture = "amd64";
            internal bool Published, LostCredentials;
            internal int ReadThrottles;
            internal string Registry = "registry.example", Repository = "project/repo";
            internal JToken CredentialExpiry = DateTime.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture);
            internal string CredentialUsername = "oauth2accesstoken", CredentialSecret = "registry-secret";
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); Paths.Add(r.RequestUri.AbsolutePath);
                string body;
                if (r.RequestUri.AbsolutePath.EndsWith("auth/cli")) body = Auth;
                else if (r.RequestUri.AbsolutePath.EndsWith("functions")) body = "{\"data\":[{\"slug\":\"tank-room\",\"kind\":\"game_server\"}],\"page\":{\"has_more\":false}}";
                else if (r.RequestUri.AbsolutePath.EndsWith(":credentials"))
                {
                    if (LostCredentials) throw new HttpRequestException("lost credentials response");
                    var credentials = new JObject { ["registry"] = Registry, ["repository"] = Repository, ["username"] = CredentialUsername, ["secret"] = CredentialSecret };
                    if (CredentialExpiry != null) credentials["expires_at"] = CredentialExpiry;
                    body = credentials.ToString();
                }
                else
                {
                    if (Published && ReadThrottles-- > 0) { var busy = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("{}") }; busy.Headers.TryAddWithoutValidation("Retry-After", "3"); return Task.FromResult(busy); }
                    var digest = ExistingDigest ?? (Published ? Digest : null);
                    var tags = digest == null ? new JArray() : new JArray(new JObject { ["tag"] = "reviewed", ["digest"] = digest, ["architecture"] = Architecture });
                    body = new JObject { ["registry"] = Registry, ["repository"] = Repository, ["images"] = new JArray(new JObject { ["name"] = "tank-room", ["tags"] = tags }) }.ToString();
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }
        }
        private sealed class Docker : IServerImageProcess
        {
            internal readonly List<string[]> Calls = new List<string[]>();
            internal string Config, Input, Architecture = "amd64";
            internal int BuildExit, PushExit;
            internal Action Pushing;
            internal bool LosePush, Missing;
            public Task<ServerImageProcessResult> RunAsync(string[] args, string directory, IReadOnlyDictionary<string, string> env, string input, TimeSpan timeout, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); Calls.Add(args);
                if (Missing) throw new System.ComponentModel.Win32Exception("not found");
                var output = ""; var exit = 0;
                if (args[0] == "info") output = "linux";
                if (args[0] == "context") output = "unix:///var/run/docker.sock";
                if (args[0] == "build") { exit = BuildExit; File.WriteAllText(args[Array.IndexOf(args, "--iidfile") + 1], Id); Assert.That(args, Does.Contain("linux/amd64")); }
                if (args[0] == "image") output = "[{\"Id\":\"" + Id + "\",\"Os\":\"linux\",\"Architecture\":\"" + Architecture + "\"}]";
                if (args[0] == "login") { Config = env["DOCKER_CONFIG"]; Input = input; Assert.That(Directory.Exists(Config), Is.True); Assert.That(args, Does.Not.Contain("registry-secret")); }
                if (args[0] == "tag") Assert.That(args[1], Is.EqualTo(Id));
                if (args[0] == "push") { Pushing?.Invoke(); if (LosePush) throw new IOException("lost connection"); exit = PushExit; output = "reviewed: digest: " + Digest + " size: 123"; }
                return Task.FromResult(new ServerImageProcessResult { ExitCode = exit, Output = output });
            }
        }
        private static PlatformFunctionClient Api(Http h) => new PlatformFunctionClient("https://platform.example", "sk_key", new HttpClient(h));
        [Test]
        public void FreshCredentialsPublishRegardlessOfCultureAndUtcOffset(
            [Values("en-US", "uk-UA", "ru-RU")] string culture,
            [Values("Z", "+00:00", "+03:00", "-05:00")] string offset,
            [Values(false, true)] bool fractionalSeconds) => Run(async () =>
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var minutes = offset == "+03:00" ? 180 : offset == "-05:00" ? -300 : 0;
                var expiry = DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromMinutes(minutes));
                var timestamp = expiry.ToString(fractionalSeconds ? "yyyy-MM-dd'T'HH:mm:ss.fffffff" : "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + offset;
                var h = new Http { CredentialExpiry = timestamp };
                var docker = new Docker { Pushing = () => h.Published = true };
                using (var api = Api(h))
                {
                    await api.ConnectAsync(default);
                    var publisher = new ServerImagePublisher(api, docker);
                    await publisher.PublishAsync(new BuiltServerImage(Id, _folder, "Dockerfile"), "tank-room", "reviewed", default);
                    Assert.That(publisher.LastPublication.Digest, Is.EqualTo(Digest));
                    Assert.That(docker.Calls.Count(args => args[0] == "push"), Is.EqualTo(1));
                    Assert.That(h.Paths.Count(path => path.EndsWith(":credentials")), Is.EqualTo(1));
                    Assert.That(Directory.Exists(docker.Config), Is.False);
                }
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        });

        [TestCase("expired")]
        [TestCase("expires-now")]
        [TestCase("missing")]
        [TestCase("null")]
        [TestCase("malformed")]
        [TestCase("object")]
        [TestCase("number")]
        [TestCase("username")]
        [TestCase("secret")]
        public void InvalidCredentialsAreRejectedBeforeDockerAuthenticationWithoutLeakingSecrets(string scenario) => Run(async () =>
        {
            var h = new Http();
            switch (scenario)
            {
                case "expired": h.CredentialExpiry = DateTimeOffset.UtcNow.AddHours(-1).ToOffset(TimeSpan.FromHours(3)).ToString("O", CultureInfo.InvariantCulture); break;
                case "expires-now": h.CredentialExpiry = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); break;
                case "missing": h.CredentialExpiry = null; break;
                case "null": h.CredentialExpiry = JValue.CreateNull(); break;
                case "malformed": h.CredentialExpiry = "registry-secret"; break;
                case "object": h.CredentialExpiry = new JObject { ["secret"] = "registry-secret" }; break;
                case "number": h.CredentialExpiry = 123; break;
                case "username": h.CredentialUsername = "registry-secret"; break;
                case "secret": h.CredentialSecret = ""; break;
            }
            var docker = new Docker();
            using (var api = Api(h))
            {
                await api.ConnectAsync(default);
                var publisher = new ServerImagePublisher(api, docker);
                var error = Assert.Throws<InvalidOperationException>(() => Run(() => publisher.PublishAsync(new BuiltServerImage(Id, _folder, "Dockerfile"), "tank-room", "reviewed", default)));
                if (scenario == "expired" || scenario == "expires-now") Assert.That(error.Message, Does.Contain("has expired"));
                else if (scenario == "username") Assert.That(error.Message, Does.Contain("invalid registry credentials"));
                else if (scenario != "secret") Assert.That(error.Message, Does.Contain("invalid expiration time"));
                Assert.That(error.Message, Does.Not.Contain("registry-secret"));
                Assert.That(error.Message, Does.Not.Contain("operator-secret"));
                Assert.That(docker.Calls.Any(args => args[0] == "login" || args[0] == "tag" || args[0] == "push"), Is.False);
                Assert.That(publisher.LastPublication, Is.Null);
                Assert.That(h.Paths.Count(path => path.EndsWith(":credentials")), Is.EqualTo(1));
            }
        });

        [Test] public void BuildPinsImageAndPublishesOnlyAfterArchitectureAndManifestVerification() => Run(async () =>
        {
            var h = new Http(); var docker = new Docker { Pushing = () => h.Published = true };
            using (var api = Api(h))
            {
                await api.ConnectAsync(default); var p = new ServerImagePublisher(api, docker);
                var image = await p.BuildAsync(_folder, "Dockerfile", default);
                Assert.That(image.Id, Is.EqualTo(Id)); Assert.That(h.Paths.Count, Is.EqualTo(1));
                await p.PublishAsync(image, "tank-room", "reviewed", default);
                Assert.That(p.LastPublication.Digest, Is.EqualTo(Digest));
                Assert.That(docker.Input.Trim(), Is.EqualTo("registry-secret")); Assert.That(Directory.Exists(docker.Config), Is.False);
                Assert.That(docker.Calls.Single(a => a[0] == "push")[1], Is.EqualTo("registry.example/project/repo/tank-room:reviewed"));
                Assert.That(h.Paths.Count(x => x.EndsWith("auth/cli")), Is.EqualTo(2));
            }
        });
        [Test] public void WrongArchitectureCannotReachCredentials() => Run(async () =>
        {
            var h = new Http(); using (var api = Api(h)) { var p = new ServerImagePublisher(api, new Docker { Architecture = "arm64" }); Assert.Throws<InvalidOperationException>(() => Run(() => p.BuildAsync(_folder, "Dockerfile", default))); Assert.That(h.Paths, Is.Empty); } await Task.CompletedTask;
        });
        [Test] public void ExistingTagNeverLogsInOrPushes() => Run(async () =>
        {
            var h = new Http { ExistingDigest = Digest }; var d = new Docker();
            using (var a = Api(h)) { await a.ConnectAsync(default); var p = new ServerImagePublisher(a, d); var image = await p.BuildAsync(_folder, "Dockerfile", default); Assert.Throws<InvalidOperationException>(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", default))); Assert.That(d.Calls.Any(x => x[0] == "login"), Is.False); Assert.That(h.Paths.Any(x => x.EndsWith(":credentials")), Is.False); }
        });
        [TestCase("../Dockerfile")][TestCase("missing")]
        public void DockerfileMustExistInsideContext(string file) { using (var a = Api(new Http())) { var p = new ServerImagePublisher(a, new Docker()); Assert.Throws<InvalidOperationException>(() => Run(() => p.BuildAsync(_folder, file, default))); } }
        [TestCase(1, false)][TestCase(0, true)]
        public void BuildFailureAndMissingDockerAreActionable(int exit, bool missing)
        { using (var a = Api(new Http())) { var p = new ServerImagePublisher(a, new Docker { BuildExit = exit, Missing = missing }); Assert.Throws<InvalidOperationException>(() => Run(() => p.BuildAsync(_folder, "Dockerfile", default))); } }
        [Test] public void UncertainPushIsNotRetriedAndCanOnlyBeCheckedWithoutDigest() => Run(async () =>
        {
            var h = new Http(); var d = new Docker { LosePush = true, Pushing = () => h.Published = true };
            using (var a = Api(h)) { await a.ConnectAsync(default); var p = new ServerImagePublisher(a, d); var image = await p.BuildAsync(_folder, "Dockerfile", default); Assert.Throws<InvalidOperationException>(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", default))); Assert.That(Directory.Exists(d.Config), Is.False); Assert.That(await p.CheckPublicationAsync(p.LastPublication, default), Is.False); Assert.That(d.Calls.Count(x => x[0] == "push"), Is.EqualTo(1)); }
        });
        [Test] public void WrongRemoteDigestIsNotSuccess() => Run(async () =>
        {
            var h = new Http(); var d = new Docker { Pushing = () => { h.Published = true; h.ExistingDigest = Id; } };
            using (var a = Api(h)) { await a.ConnectAsync(default); var p = new ServerImagePublisher(a, d); var image = await p.BuildAsync(_folder, "Dockerfile", default); Assert.Throws<InvalidOperationException>(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", default))); Assert.That(Directory.Exists(d.Config), Is.False); }
        });
        [Test] public void VerificationHonorsRetryAfter() => Run(async () =>
        {
            var h = new Http { ReadThrottles = 1 }; var waits = new List<TimeSpan>(); var d = new Docker { Pushing = () => h.Published = true };
            using (var a = Api(h)) { await a.ConnectAsync(default); var p = new ServerImagePublisher(a, d, (t, ct) => { waits.Add(t); return Task.CompletedTask; }); var image = await p.BuildAsync(_folder, "Dockerfile", default); await p.PublishAsync(image, "tank-room", "reviewed", default); Assert.That(waits, Does.Contain(TimeSpan.FromSeconds(3))); }
        });
        [Test] public void CancelledPushCleansAuthenticationAndDoesNotVerify() => Run(async () =>
        {
            var h = new Http(); using (var cancel = new CancellationTokenSource()) using (var a = Api(h)) { await a.ConnectAsync(default); var d = new Docker { Pushing = () => cancel.Cancel() }; var p = new ServerImagePublisher(a, d); var image = await p.BuildAsync(_folder, "Dockerfile", default); Assert.That(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", cancel.Token)), Throws.InstanceOf<OperationCanceledException>()); Assert.That(Directory.Exists(d.Config), Is.False); }
        });
        [Test] public void FailedPushCleansCredentialsAndBlocksAnAutomaticSecondAttempt() => Run(async () =>
        {
            var h = new Http(); var d = new Docker { PushExit = 1 };
            using (var a = Api(h))
            {
                await a.ConnectAsync(default); var p = new ServerImagePublisher(a, d); var image = await p.BuildAsync(_folder, "Dockerfile", default);
                Assert.Throws<InvalidOperationException>(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", default)));
                Assert.That(Directory.Exists(d.Config), Is.False);
                Assert.Throws<InvalidOperationException>(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", default)));
                Assert.That(d.Calls.Count(x => x[0] == "push"), Is.EqualTo(1));
            }
        });
        [Test] public void RemoteWrongArchitectureIsNeverVerified() => Run(async () =>
        {
            var h = new Http { Architecture = "arm64" }; var d = new Docker { Pushing = () => h.Published = true };
            using (var a = Api(h))
            {
                await a.ConnectAsync(default); var p = new ServerImagePublisher(a, d); var image = await p.BuildAsync(_folder, "Dockerfile", default);
                Assert.Throws<InvalidOperationException>(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", default)));
            }
        });
        [Test] public void PublicationDeadlineDoesNotRepeatPush() => Run(async () =>
        {
            var h = new Http(); var d = new Docker();
            using (var a = Api(h))
            {
                await a.ConnectAsync(default); var p = new ServerImagePublisher(a, d, verificationBudget: TimeSpan.FromMilliseconds(40));
                var image = await p.BuildAsync(_folder, "Dockerfile", default);
                Assert.Throws<TimeoutException>(() => Run(() => p.PublishAsync(image, "tank-room", "reviewed", default)));
                Assert.That(Directory.Exists(d.Config), Is.False); Assert.That(d.Calls.Count(x => x[0] == "push"), Is.EqualTo(1));
            }
        });
        [Test] public void PublicationCannotBeCheckedFromAnotherAuthorizationScope() => Run(async () =>
        {
            var h = new Http(); using (var a = Api(h))
            {
                await a.ConnectAsync(default); var p = new ServerImagePublisher(a, new Docker());
                var foreign = new ServerImagePublication { Target = "https://other.example|tanks|dev", Server = "tank-room", Tag = "reviewed" };
                Assert.Throws<InvalidOperationException>(() => Run(() => p.CheckPublicationAsync(foreign, default)));
                Assert.That(h.Paths.Count, Is.EqualTo(1));
            }
        });
        [TestCase("not-registered", "reviewed")][TestCase("tank-room", "bad/tag")]
        public void InvalidTargetCannotMintCredentials(string server, string tag) => Run(async () =>
        {
            var h = new Http(); using (var a = Api(h)) { await a.ConnectAsync(default); var p = new ServerImagePublisher(a, new Docker()); var image = await p.BuildAsync(_folder, "Dockerfile", default); Assert.Throws<InvalidOperationException>(() => Run(() => p.PublishAsync(image, server, tag, default))); Assert.That(h.Paths.Any(x => x.EndsWith(":credentials")), Is.False); }
        });
    }
}
