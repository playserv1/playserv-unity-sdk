using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;
using static Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServRoomCreateAttributesTests
    {
        private PlayServGameServerUplinkTests.Http _http;
        private Socket _socket;
        private AdmissionTestScope _scope;
        private ConcurrentQueue<PlayServRoomCreationOutcome> _outcomes;
        private ConcurrentQueue<PlayServError> _errors;
        private readonly IJsonCodec _codec = new NewtonsoftJsonCodec();
        private int _calls;

        [SetUp] public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown();
            _http = new PlayServGameServerUplinkTests.Http(); _socket = new Socket(Ack()); _scope = new AdmissionTestScope();
            _outcomes = new ConcurrentQueue<PlayServRoomCreationOutcome>();
            _errors = new ConcurrentQueue<PlayServError>(); _calls = 0;
        }
        [UnityTearDown] public IEnumerator Teardown() => Run(() => _scope.CleanupAsync());
        private void Configure(PlayServRoomFactory factory, int timeoutMs = 5000)
        {
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", InstanceId = "unity-attributes",
                ServerKeyProvider = new Key(), RoomCreateTimeout = TimeSpan.FromMilliseconds(timeoutMs),
                RoomFactory = (request, ct) => { Interlocked.Increment(ref _calls); return factory(request, ct); }
            }, _http, () => DateTimeOffset.UtcNow, (_, ct) => Task.Delay(Timeout.Infinite, ct));
            var uplink = PlayServGameServer.Uplink;
            uplink.SocketFactory = () => _socket; _scope.Track(uplink);
            var outcomes = _outcomes; var errors = _errors;
            Action<PlayServRoomCreationOutcome> completed = outcomes.Enqueue;
            Action<PlayServError> onError = errors.Enqueue;
            uplink.RoomCreationCompleted += completed; uplink.OnError += onError;
            _scope.UnsubscribeOnCleanup(() => { uplink.RoomCreationCompleted -= completed; uplink.OnError -= onError; });
        }
        private static object Attributes(PlayServRoomCreateContext request) => request.Attributes;
        private static PlayServRoomCreateDecision Prepared(string name, object attributes = null) =>
            PlayServRoomCreateDecision.Accept(new PlayServGameRoomSnapshot(name, 0, 8, attributes: attributes,
                connect: new PlayServGameRoomConnect("game.test", 7777, "udp")));
        private void Request(string name, string attributes = null) => _socket.Push(
            "{\"type\":\"room_create\",\"room_name\":\"" + name + "\"" + (attributes == null ? "" : ",\"attributes\":" + attributes) + "}");
        private Task Completed(int count) => Until(() => _outcomes.Count == count,
            () => "uplink=" + PlayServGameServer.Uplink.State + ", outcomes=" + _outcomes.Count + ", errors=" + _errors.Count);
        private string[] Results() => _socket.Sent.Where(s => s.Contains("\"type\":\"room_create_result\"")).ToArray();
        private IDictionary<string, object> RegisteredAttributes() =>
            (IDictionary<string, object>)((IDictionary<string, object>)_codec.ParseToPlainValue(_http.Requests.Last().JsonBody))["attributes"];

        [UnityTest] public IEnumerator FactoryReceivesScalarJsonTypesAndIndependentSnapshots() => Run(async () =>
        {
            PlayServRoomCreateContext captured = null;
            Configure((request, ct) => { captured = request; return Task.FromResult(Prepared(request.RoomName)); });
            await PlayServGameServer.Uplink.ConnectAsync();
            Request("types", "{\"map\":\"forest\",\"ranked\":true,\"difficulty\":2.5,\"players\":8,\"nil\":null,\"date\":\"2026-09-15T12:00:00Z\",\"$type\":\"game-content\"}");
            await Completed(1);
            var attributes = (IDictionary<string, object>)Attributes(captured);
            Assert.That(attributes["map"], Is.EqualTo("forest")); Assert.That(attributes["ranked"], Is.TypeOf<bool>());
            Assert.That(attributes["difficulty"], Is.EqualTo(2.5)); Assert.That(attributes["players"], Is.EqualTo(8L));
            Assert.That(attributes["nil"], Is.Null); Assert.That(attributes["date"], Is.TypeOf<string>());
            Assert.That(attributes["date"], Is.EqualTo("2026-09-15T12:00:00Z"));
            Assert.That(attributes["$type"], Is.EqualTo("game-content"));
            attributes["map"] = "changed"; attributes.Clear();
            Assert.That(((IDictionary<string, object>)Attributes(captured))["map"], Is.EqualTo("forest"));
            Assert.That(_outcomes.Single().IsSuccess, Is.True);
        });

        [UnityTest] public IEnumerator FactoryCanApplyRequestedAttributesToRegistration() => Run(async () =>
        {
            Configure((request, ct) =>
            {
                var approved = (IDictionary<string, object>)Attributes(request);
                var decision = Prepared(request.RoomName, approved);
                approved["map"] = "changed-after-snapshot";
                return Task.FromResult(decision);
            });
            await PlayServGameServer.Uplink.ConnectAsync(); Request("apply", "{\"map\":\"forest\",\"ranked\":true,\"difficulty\":2.5}");
            await Completed(1);
            Assert.That(_outcomes.Single().IsSuccess, Is.True);
            Assert.That(RegisteredAttributes()["map"], Is.EqualTo("forest"));
            Assert.That(RegisteredAttributes()["ranked"], Is.EqualTo(true));
            Assert.That(RegisteredAttributes()["difficulty"], Is.EqualTo(2.5));
            Assert.That(_http.Requests.Single().RelativePath, Is.EqualTo("rooms/arena:upsert"));
            Assert.That(Results().Single(), Is.EqualTo("{\"type\":\"room_create_result\",\"room_name\":\"apply\",\"ok\":true}"));
        });

        [UnityTest] public IEnumerator FactoryCanAlterAttributesWithoutAutomaticMerge() => Run(async () =>
        {
            Configure((request, ct) =>
            {
                var wish = (IDictionary<string, object>)Attributes(request);
                Assert.That(wish["map"], Is.EqualTo("forest"));
                return Task.FromResult(Prepared(request.RoomName, new { map = "arena", mode = "practice" }));
            });
            await PlayServGameServer.Uplink.ConnectAsync(); Request("alter", "{\"map\":\"forest\",\"untrusted\":true}"); await Completed(1);
            Assert.That(RegisteredAttributes().Count, Is.EqualTo(2));
            Assert.That(RegisteredAttributes()["map"], Is.EqualTo("arena"));
            Assert.That(RegisteredAttributes().ContainsKey("untrusted"), Is.False);
        });

        [UnityTest] public IEnumerator LegacyFactoryCanIgnoreAttributesAndKeepsItsOwnSnapshot() => Run(async () =>
        {
            Configure((request, ct) => Task.FromResult(Prepared(request.RoomName)));
            await PlayServGameServer.Uplink.ConnectAsync(); Request("ignore", "{\"map\":\"forest\"}"); await Completed(1);
            Assert.That(_outcomes.Single().IsSuccess, Is.True); Assert.That(RegisteredAttributes(), Is.Null);
        });

        [UnityTest] public IEnumerator AbsentNullAndEmptyAttributesRemainDistinctAndCompatible() => Run(async () =>
        {
            var seen = new ConcurrentQueue<PlayServRoomCreateContext>();
            Configure((request, ct) => { seen.Enqueue(request); return Task.FromResult(Prepared(request.RoomName)); });
            await PlayServGameServer.Uplink.ConnectAsync();
            foreach (var json in new[] { null, "null", "{}" })
            {
                var count = _outcomes.Count + 1;
                Request("shape-" + count, json); await Completed(count);
                Assert.That(_outcomes.Last().IsSuccess, Is.True);
                var value = Attributes(seen.Last());
                if (json == "{}") Assert.That((IDictionary<string, object>)value, Is.Empty);
                else Assert.That(value, Is.Null);
                await _outcomes.Last().Room.CloseAsync();
            }
        });

        [UnityTest] public IEnumerator ContentRefusalReleasesNameAndAllowsTheNextRequest() => Run(async () =>
        {
            Configure((request, ct) => Task.FromResult(_calls == 1
                ? PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.ContentRefused, "map unavailable") : Prepared(request.RoomName)));
            await PlayServGameServer.Uplink.ConnectAsync(); Request("refused", "{\"map\":\"unsupported\"}"); await Completed(1);
            Assert.That(Results().Single(), Is.EqualTo("{\"type\":\"room_create_result\",\"room_name\":\"refused\",\"ok\":false,\"reason\":\"content_refused\",\"detail\":\"map unavailable\"}"));
            Assert.That(_http.Requests, Is.Empty); Assert.That(_outcomes.Single().FactoryInvoked, Is.True);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("content_refused"));
            Assert.That(PlayServGameServer.Uplink.AcceptingRoomRequests, Is.True);
            Request("refused", "{\"map\":\"arena\"}"); await Completed(2);
            Assert.That(_outcomes.Last().IsSuccess, Is.True); Assert.That(_calls, Is.EqualTo(2));
            Assert.That(Results().Length, Is.EqualTo(2)); Assert.That(_http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator InvalidAttributesAreDroppedWithoutFactoryOrWireRefusal() => Run(async () =>
        {
            Configure((request, ct) => Task.FromResult(Prepared(request.RoomName)));
            await PlayServGameServer.Uplink.ConnectAsync();
            foreach (var json in new[] { "[]", "\"{}\"", "1", "true", "{\"nested\":{}}", "{\"nested\":[]}", "{\"bad\":NaN}" })
            {
                var count = _errors.Count + 1;
                Request("invalid", json);
                await Until(() => _errors.Count == count || _calls > 0);
                Assert.That(_calls, Is.Zero); Assert.That(_errors.Last().SourceCode, Is.EqualTo("room_create_invalid_request"));
            }
            Assert.That(_http.Requests, Is.Empty); Assert.That(Results(), Is.Empty); Assert.That(_outcomes, Is.Empty);
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Connected));
        });

        [UnityTest] public IEnumerator SerializedUtf8AttributeLimitAccepts2048AndRejects2049() => Run(async () =>
        {
            Configure((request, ct) => Task.FromResult(Prepared(request.RoomName)));
            await PlayServGameServer.Uplink.ConnectAsync();
            foreach (var content in new[] { new string('a', 2037), new string('я', 1018) + "a" })
            {
                var exact = "{\"data\":\"" + content + "\"}";
                Assert.That(Encoding.UTF8.GetByteCount(exact), Is.EqualTo(2048));
                var count = _outcomes.Count + 1;
                Request("limit-" + count, exact); await Completed(count);
                Assert.That(_outcomes.Last().IsSuccess, Is.True); await _outcomes.Last().Room.CloseAsync();
                var tooLarge = "{\"data\":\"" + content + "x\"}";
                var errors = _errors.Count; var calls = _calls;
                Request("too-large", tooLarge); await Until(() => _errors.Count > errors || _calls > calls);
                Assert.That(_calls, Is.EqualTo(calls)); Assert.That(_errors.Last().SourceCode, Is.EqualTo("room_create_invalid_request"));
            }
        });

        [UnityTest] public IEnumerator AttributesAreCapturedBeforeUnityContextDispatch() => Run(async () =>
        {
            var deliveries = new ConcurrentQueue<Action>(); PlayServRoomCreateContext seen = null;
            Configure((request, ct) => { seen = request; return Task.FromResult(Prepared(request.RoomName, Attributes(request))); });
            PlayServGameServer.GetContextForServices().Dispatch = deliveries.Enqueue;
            await PlayServGameServer.Uplink.ConnectAsync(); Request("deferred", "{\"map\":\"forest\"}");
            await Until(() =>
            {
                while (deliveries.TryDequeue(out var action)) action();
                return _outcomes.Count == 1;
            });
            Assert.That(((IDictionary<string, object>)Attributes(seen))["map"], Is.EqualTo("forest"));
            Assert.That(RegisteredAttributes()["map"], Is.EqualTo("forest"));
        });

        [UnityTest] public IEnumerator TimeoutWithAttributesNeverSendsLateContentRefusal() => Run(async () =>
        {
            var pending = new TaskCompletionSource<PlayServRoomCreateDecision>();
            Configure((request, ct) => pending.Task, 100);
            await PlayServGameServer.Uplink.ConnectAsync(); Request("late", "{\"map\":\"forest\"}"); await Until(() => _calls == 1);
            await Completed(1); await PlayServGameServer.Uplink.DrainRoomCreationAsync();
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_create_timeout"));
            pending.SetResult(PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.ContentRefused));
            Assert.That(Results(), Is.Empty); Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator DisconnectWithAttributesDoesNotRegisterOrSendLateResult() => Run(async () =>
        {
            var pending = new TaskCompletionSource<PlayServRoomCreateDecision>();
            Configure((request, ct) => pending.Task);
            await PlayServGameServer.Uplink.ConnectAsync(); Request("canceled", "{\"map\":\"forest\"}"); await Until(() => _calls == 1);
            await PlayServGameServer.Uplink.DisconnectAsync(); await Completed(1);
            pending.SetResult(Prepared("canceled", new { map = "forest" }));
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_create_canceled"));
            Assert.That(Results(), Is.Empty); Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator AttributeAndFactorySecretsDoNotReachDiagnostics() => Run(async () =>
        {
            Configure((request, ct) => throw new Exception("sk_factory_secret a.b.c"));
            await PlayServGameServer.Uplink.ConnectAsync();
            Request("safe", "{\"map\":\"sk_attribute_secret\",\"token\":\"pk_attribute_secret\"}"); await Completed(1);
            var diagnostics = string.Join("|", _socket.Sent.Concat(_errors.Select(e => e.SourceCode + e.Message + e.RawDetails)));
            Assert.That(diagnostics, Does.Not.Contain("sk_attribute_secret").And.Not.Contain("pk_attribute_secret").And.Not.Contain("sk_factory_secret").And.Not.Contain("a.b.c"));
            Assert.That(Results().Single(), Does.Contain("\"reason\":\"room_create_failed\"").And.Contain("\"detail\":\"Exception\""));
        });

        [Test] public void ContentRefusalIsAdditiveAndKeepsTheExistingDetailRules()
        {
            Assert.That((int)PlayServRoomCreateRefusal.RoomNameConflict, Is.EqualTo(0));
            Assert.That((int)PlayServRoomCreateRefusal.InstanceDraining, Is.EqualTo(1));
            Assert.That((int)PlayServRoomCreateRefusal.RoomQuotaExceeded, Is.EqualTo(2));
            Assert.That((int)PlayServRoomCreateRefusal.RoomCreateFailed, Is.EqualTo(3));
            Assert.That((int)PlayServRoomCreateRefusal.ContentRefused, Is.EqualTo(4));
            Assert.That(PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.ContentRefused, new string('a', 128)).Reason, Is.EqualTo("content_refused"));
            Assert.Throws<ArgumentException>(() => PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.ContentRefused, new string('a', 129)));
            Assert.Throws<ArgumentException>(() => PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.ContentRefused, "invalid\ndetail"));
        }
    }
}
