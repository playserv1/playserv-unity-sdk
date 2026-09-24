using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using static Playserv.Editor.Tests.DeploymentLayoutTests;

namespace Playserv.Editor.Tests
{
    public sealed class ServerRegistrationTests
    {
        private const string Auth = "{\"access_token\":\"operator-fixture\",\"project_slug\":\"tanks\",\"env\":\"dev\"}";
        private const string Created = "{\"slug\":\"tank-room\",\"kind\":\"game_server\"}";
        private const string Listing = "{\"data\":[{\"slug\":\"tank-room\",\"kind\":\"game_server\"}],\"page\":{\"has_more\":false}}";
        private sealed class Handler : HttpMessageHandler
        {
            internal readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Replies = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
            internal int Posts;
            internal void Add(string json, HttpStatusCode status = HttpStatusCode.OK) => Replies.Enqueue((_, __) => Task.FromResult(Json(json, status)));
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                if (r.Method == HttpMethod.Post && r.RequestUri.AbsolutePath == "/api/v1/functions") Posts++;
                return Replies.Dequeue()(r, ct);
            }
        }
        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new HttpResponseMessage(status) { Content = new StringContent(json) };
        private PlayServConfig _config;
        private string _key, _server;
        private ServerImagePanel _panel;
        private Handler _handler;
        [SetUp] public void Setup()
        {
            _key = System.Environment.GetEnvironmentVariable("PLAYSERV_API_KEY");
            System.Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", "sk_registration_fixture");
            _server = ServerImageEditorStore.Server;
            _config = ScriptableObject.CreateInstance<PlayServConfig>();
            _panel = new ServerImagePanel { Config = () => _config }; Call(_panel, "Update");
            _handler = new Handler(); _handler.Add(Auth);
            var connection = Get<PlatformFunctionConnection>(_panel, "_connection");
            connection.Create("https://platform.example", "sk_registration_fixture", new HttpClient(_handler));
            var session = Task.Run(() => connection.Client.ConnectAsync(default)).GetAwaiter().GetResult();
            Set(_panel, "_session", session);
            Set(_panel, "_publisher", new ServerImagePublisher(connection.Client, new ServerImageProcess(connection.Client.Redact)));
            Get<ServerImageDraft>(_panel, "_draft").CompleteConnection(Array.Empty<string>());
        }
        [TearDown] public void Cleanup()
        {
            _panel.Dispose(); UnityEngine.Object.DestroyImmediate(_config);
            System.Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", _key);
            ServerImageEditorStore.Server = _server;
        }
        private Task<Action> Create(CancellationToken ct)
        {
            Set(_panel, "_serverName", "Tank Room"); Set(_panel, "_serverSlug", "tank-room");
            return Invoke<Task<Action>>(_panel, "CreateServerAsync", ct);
        }
        private void Start() => Call(_panel, "Start", "Creating…", new Func<CancellationToken, Task<Action>>(Create));
        private IEnumerator Complete()
        {
            var deadline = EditorApplication.timeSinceStartup + 5;
            while (_panel.Running && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(_panel.Running, Is.False);
        }
        [UnityTest] public IEnumerator CreationRefreshesListSelectsServerAndInvalidatesBuild()
        {
            _handler.Add(Auth); _handler.Add(Created); _handler.Add(Listing);
            Set(_panel, "_built", new BuiltServerImage("sha256:old", "context", "Dockerfile"));
            Start(); yield return Complete();
            Assert.That(_handler.Posts, Is.EqualTo(1), Get<string>(_panel, "_status"));
            Assert.That(Get<ServerImageDraft>(_panel, "_draft").Server, Is.EqualTo("tank-room"));
            Assert.That(ServerImageEditorStore.Server, Is.EqualTo("tank-room"));
            Assert.That(Get<BuiltServerImage>(_panel, "_built"), Is.Null);
        }
        [UnityTest] public IEnumerator ConflictRefreshesListWithoutChangingTheExistingServer()
        {
            _handler.Add(Auth); _handler.Add("{\"detail\":\"already exists\"}", HttpStatusCode.Conflict); _handler.Add(Listing);
            Start(); yield return Complete();
            Assert.That(_handler.Posts, Is.EqualTo(1), Get<string>(_panel, "_status"));
            Assert.That(Get<ServerImageDraft>(_panel, "_draft").Servers, Is.EqualTo(new[] { "tank-room" }));
            Assert.That(Get<string>(_panel, "_status"), Does.Contain("already exists"));
        }
        [UnityTest] public IEnumerator CreatedServerWithFailedRefreshIsNotCreatedAgain()
        {
            _handler.Add(Auth); _handler.Add(Created); _handler.Add("{}", HttpStatusCode.ServiceUnavailable);
            Start(); yield return Complete();
            Assert.That(_handler.Posts, Is.EqualTo(1), Get<string>(_panel, "_status"));
            Assert.That(Get<string>(_panel, "_status"), Does.Contain("created").And.Contain("refresh"));
            Assert.That(Get<bool>(_panel, "_showCreateServer"), Is.False);
        }
        [UnityTest] public IEnumerator LostResponseExplainsUncertaintyWithoutRetrying()
        {
            _handler.Add(Auth); _handler.Replies.Enqueue((_, __) => throw new HttpRequestException("lost"));
            Start(); yield return Complete();
            Assert.That(_handler.Posts, Is.EqualTo(1), Get<string>(_panel, "_status"));
            Assert.That(Get<string>(_panel, "_status"), Does.Contain("refresh servers").And.Contain("before trying again"));
        }
        [UnityTest] public IEnumerator DoubleClickCancellationAndLateCreateDoNotSelectServer()
        {
            _handler.Add(Auth);
            var response = new TaskCompletionSource<HttpResponseMessage>();
            _handler.Replies.Enqueue((_, __) => response.Task);
            Start(); Assert.That(_panel.Running, Is.True, Get<string>(_panel, "_status"));
            Start(); Assert.That(_handler.Posts, Is.EqualTo(1));
            Get<ServerImageOperation>(_panel, "_operation").Cancel();
            response.SetResult(Json(Created)); yield return Complete();
            Assert.That(Get<ServerImageDraft>(_panel, "_draft").Server, Is.Empty);
            Assert.That(_handler.Posts, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ChangedTokenDiscardsAnInFlightRegistration()
        {
            _handler.Add(Auth);
            var response = new TaskCompletionSource<HttpResponseMessage>();
            _handler.Replies.Enqueue((_, __) => response.Task);
            Start(); Assert.That(_panel.Running, Is.True, Get<string>(_panel, "_status"));
            System.Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", "sk_changed_fixture"); Call(_panel, "Update");
            response.SetResult(Json(Created)); yield return Complete();
            Assert.That(Get<ServerImageDraft>(_panel, "_draft").Servers, Is.Null);
            Assert.That(Get<ServerImagePublisher>(_panel, "_publisher"), Is.Null);
            Assert.That(Get<string>(_panel, "_status"), Does.Contain("settings changed"));
        }
        [UnityTest] public IEnumerator ClosingWindowDiscardsAnInFlightRegistration()
        {
            _handler.Add(Auth);
            var response = new TaskCompletionSource<HttpResponseMessage>();
            _handler.Replies.Enqueue((_, __) => response.Task);
            Start(); Assert.That(_panel.Running, Is.True);
            _panel.Dispose(); response.SetResult(Json(Created)); yield return Complete();
            Assert.That(Get<ServerImageDraft>(_panel, "_draft").Server, Is.Empty);
        }
        [UnityTest] public IEnumerator UnansweredCreateTimesOutWithoutRetrying()
        {
            _handler.Add(Auth);
            _handler.Replies.Enqueue(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json(Created); });
            Start();
            var deadline = EditorApplication.timeSinceStartup + 40;
            while (_panel.Running && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(_panel.Running, Is.False);
            Assert.That(_handler.Posts, Is.EqualTo(1));
            Assert.That(Get<string>(_panel, "_status"), Does.Contain("timed out").And.Contain("before trying again"));
        }
    }
}
