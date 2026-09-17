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
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServInboundRpcTests
    {
        private H.AdmissionTestScope _scope;
        private ConcurrentQueue<H.Socket> _sockets;
        private H.Socket Socket => _sockets.Last();
        private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
        private PlayServGameServerUplink Uplink => PlayServGameServer.Uplink;
        [SetUp] public void Setup() { _scope = new H.AdmissionTestScope(); }
        [UnityTearDown] public IEnumerator Cleanup() => H.Run(() => _scope.CleanupAsync());

        private async Task Configure(PlayServServerRpcRegistry registry, int timeoutMs = 3000)
        {
            PlayServGameServer.CancelForModuleShutdown();
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(),
                EnablePushedAdmission = false, RpcRegistry = registry, HttpTimeout = TimeSpan.FromMilliseconds(timeoutMs) },
                new H.Http(), () => DateTimeOffset.UtcNow, (_, ct) => Task.Delay(Timeout.Infinite, ct));
            var delivery = SynchronizationContext.Current;
            Assert.That(delivery, Is.Not.Null);
            PlayServGameServer.GetContextForServices().Dispatch = action => delivery.Post(_ => action(), null);
            _scope.Track(Uplink);
            _sockets = new ConcurrentQueue<H.Socket>();
            var sockets = _sockets;
            Uplink.SocketFactory = () => { var socket = new H.Socket(H.Ack()); sockets.Enqueue(socket); return socket; };
            await Uplink.ConnectAsync();
        }
        private object Frame(string id, string payload = "[7]", string method = "sum", bool oneWay = false) => new
        { type = "rpc_call", id, method_name = method, payload_base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)),
            one_way = oneWay, player_id = "plr_real", caller_kind = "player", caller_key_id = "key-one", project_id = "prj_one", env = "dev",
            display_name = "Player", player_providers = "anonymous", received_at_unix_ms = 1234L, player_jwt = "secret.jwt.value" };
        private void Call(string id, string payload = "[7]", string method = "sum", bool oneWay = false) => Socket.Push(_json.Serialize(Frame(id, payload, method, oneWay)));
        private string[] Replies(H.Socket socket = null) => (socket ?? Socket).Sent.Where(s => s.Contains("\"type\":\"rpc_result\"")).ToArray();
        private Task Until(Func<bool> predicate) => H.Until(predicate, () => $"Uplink={Uplink.State}; pending RPC={Uplink.PendingRpcCalls}; replies={Replies().Length}.");
        private async Task Barrier()
        {
            var socket = Socket; var count = socket.Sent.Count(s => s == "{\"type\":\"pong\"}");
            socket.Push("{\"type\":\"ping\"}");
            await Until(() => socket.Sent.Count(s => s == "{\"type\":\"pong\"}") == count + 1);
        }
        private static TaskCompletionSource<T> Source<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        [UnityTest] public IEnumerator TypedBindingUsesAuthenticatedContextAndUnityDelivery() => H.Run(async () =>
        {
            var main = Thread.CurrentThread.ManagedThreadId;
            var value = new PlayServServerRpcParameter<int>("value");
            var factor = new PlayServServerRpcParameter<int>("factor", false, 2);
            var registry = new PlayServServerRpcRegistry();
            var calls = 0;
            var threads = new ConcurrentQueue<int>();
            var contexts = new ConcurrentQueue<PlayServServerRpcContext>();
            registry.Register("sum", new PlayServServerRpcParameter[] { value, factor }, (context, args, ct) =>
            {
                threads.Enqueue(Thread.CurrentThread.ManagedThreadId); contexts.Enqueue(context);
                calls++; return Task.FromResult(args.Get(value) * args.Get(factor));
            });
            await Configure(registry);
            Assert.That(Socket.Sent.First(), Does.Contain("\"rpc\""));
            registry.Register("late", Array.Empty<PlayServServerRpcParameter>(), (c, a, ct) => Task.FromResult(1));
            Call("one"); Call("two", "{\"value\":3,\"factor\":4}"); Call("late", "[]", "late");
            await Until(() => Replies().Length == 3);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(threads.All(thread => thread == main), Is.True, "Handler must use the configured Unity context.");
            foreach (var context in contexts)
            {
                Assert.That(context.PlayerId, Is.EqualTo("plr_real")); Assert.That(context.Environment, Is.EqualTo("dev"));
                Assert.That(context.CallerKind, Is.EqualTo("player")); Assert.That(context.ReceivedAtUnixMs, Is.EqualTo(1234));
            }
            Assert.That(Replies().Single(s => s.Contains("\"id\":\"one\"")), Does.Contain("\"result\":14"));
            Assert.That(Replies().Single(s => s.Contains("\"id\":\"two\"")), Does.Contain("\"result\":12"));
            Assert.That(Replies().Single(s => s.Contains("\"id\":\"late\"")), Does.Contain("rpc_method_not_found"));
            Assert.That(string.Join("", Replies()), Does.Not.Contain("secret.jwt.value"));
        });

        [UnityTest] public IEnumerator InvalidArgumentsNeverReachHandlerAndErrorsDoNotLeakPayload() => H.Run(async () =>
        {
            var parameter = new PlayServServerRpcParameter<int>("value"); var registry = new PlayServServerRpcRegistry(); var called = 0;
            registry.Register("sum", new[] { parameter }, (c, a, ct) => { called++; throw new Exception("sk_private_secret"); return Task.FromResult(1); });
            await Configure(registry);
            foreach (var payload in new[] { "[\"7\"]", "[1.5]", "[2147483648]", "[null]", "[]", "[1,2]", "false", "{\"value\":1,\"extra\":2}", "{\"value\":1,\"value\":2}", "{" })
                Call(Guid.NewGuid().ToString("N"), payload);
            Call("throws", "[1]");
            Socket.Push("{\"type\":\"rpc_call\",\"id\":\"bad-base64\",\"method_name\":\"sum\",\"payload_base64\":\"!!!!\"}");
            await Until(() => Replies().Length == 12);
            Assert.That(called, Is.EqualTo(1)); Assert.That(Replies().All(s => s.Contains("\"status\":\"error\"")), Is.True);
            Assert.That(string.Join("", Replies()), Does.Not.Contain("sk_private_secret"));
        });

        [UnityTest] public IEnumerator BatchOneWayAndActiveDuplicateAreSerial() => H.Run(async () =>
        {
            var parameter = new PlayServServerRpcParameter<int>("value"); var registry = new PlayServServerRpcRegistry();
            var release = Source<int>(); var entered = Source<bool>(); var calls = new List<int>();
            registry.Register("sum", new[] { parameter }, async (c, a, ct) =>
            { calls.Add(a.Get(parameter)); if (calls.Count == 1) { entered.TrySetResult(true); await release.Task; } return a.Get(parameter); });
            await Configure(registry);
            Socket.Push(_json.Serialize(new { type = "rpc_batch", calls = new[] { Frame("a", "[1]"), Frame("b", "[2]", oneWay: true), Frame("c", "[3]") } }));
            await entered.Task;
            Call("a", "[99]"); await Barrier(); Assert.That(calls, Is.EqualTo(new[] { 1 }));
            release.TrySetResult(1); await Until(() => Uplink.PendingRpcCalls == 0 && Replies().Length == 2);
            Assert.That(calls, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(Replies()[0], Does.Contain("\"id\":\"a\"")); Assert.That(Replies()[1], Does.Contain("\"id\":\"c\""));
        });

        [UnityTest] public IEnumerator QueueCountAndRetainedBytesIncludeActiveCall() => H.Run(async () =>
        {
            var registry = new PlayServServerRpcRegistry(); var release = Source<int>(); var entered = Source<bool>();
            var parameter = new PlayServServerRpcParameter<string>("value");
            registry.Register("sum", new[] { parameter }, async (c, a, ct) => { entered.TrySetResult(true); return await release.Task; });
            await Configure(registry);
            Call("active", "[\"a\"]"); await entered.Task;
            for (var i = 0; i < 64; i++) Call("q" + i, "[\"a\"]");
            await Barrier(); await Until(() => Replies().Length == 1);
            Assert.That(Uplink.PendingRpcCalls, Is.EqualTo(64)); Assert.That(Replies()[0], Does.Contain("rpc_queue_full"));
            release.TrySetResult(1); await Until(() => Uplink.PendingRpcCalls == 0);
            await Uplink.DisconnectAsync();
            release = Source<int>(); entered = Source<bool>();
            await Configure(registry);
            var big = _json.Serialize(new[] { new string('a', 420000) });
            Call("large-one", big); await entered.Task; Call("large-two", big);
            await Barrier(); await Until(() => Replies().Length == 1);
            Assert.That(Uplink.PendingRpcCalls, Is.EqualTo(1)); Assert.That(Replies()[0], Does.Contain("rpc_queue_full"));
            release.TrySetResult(1); await Until(() => Uplink.PendingRpcCalls == 0);
        });

        [UnityTest] public IEnumerator TimeoutCancelsWithoutOverlappingAnUncooperativeHandler() => H.Run(async () =>
        {
            var registry = new PlayServServerRpcRegistry(); var release = Source<int>(); var canceled = Source<bool>(); var count = 0;
            registry.Register("sum", Array.Empty<PlayServServerRpcParameter>(), async (c, a, ct) =>
            { count++; using (ct.Register(() => canceled.TrySetResult(true))) return await release.Task; });
            await Configure(registry, 100);
            Call("first", "[]"); await Until(() => count == 1); Call("second", "[]");
            await canceled.Task; await Until(() => Replies().Length == 2);
            Assert.That(count, Is.EqualTo(1)); Assert.That(Uplink.PendingRpcCalls, Is.EqualTo(1));
            Assert.That(Replies().All(s => s.Contains("rpc_timeout")), Is.True);
            release.TrySetResult(5); await Until(() => Uplink.PendingRpcCalls == 0); await Barrier(); Assert.That(Replies().Length, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator DisconnectCancelsAndNeverSendsLateResultOnNewSocket() => H.Run(async () =>
        {
            var registry = new PlayServServerRpcRegistry(); var release = Source<int>(); var canceled = Source<bool>(); var entered = Source<bool>();
            registry.Register("sum", Array.Empty<PlayServServerRpcParameter>(), async (c, a, ct) =>
            { using (ct.Register(() => canceled.TrySetResult(true))) { entered.TrySetResult(true); return await release.Task; } });
            await Configure(registry); var old = Socket;
            Call("old", "[]"); await entered.Task; await Uplink.DisconnectAsync(); await canceled.Task;
            await Uplink.ConnectAsync(); Assert.That(Socket, Is.Not.SameAs(old));
            release.TrySetResult(42); await Until(() => Uplink.PendingRpcCalls == 0); await Barrier();
            Assert.That(Replies(old), Is.Empty); Assert.That(Replies(), Is.Empty);
        });

        [Test] public void ExplicitDtoDescriptorsAreStrictAndPreservedForAot()
        {
            var parameter = new PlayServServerRpcParameter<RpcDto>("dto");
            var registry = new PlayServServerRpcRegistry();
            registry.Register<RpcDto>("roundtrip", new[] { parameter }, (c, a, ct) => Task.FromResult(a.Get(parameter)));
            var method = registry.Snapshot()["roundtrip"];
            var args = method.Bind(Convert.ToBase64String(Encoding.UTF8.GetBytes("[{\"Count\":5,\"Names\":[\"a\"]}]")));
            Assert.That(args.Get(parameter).Count, Is.EqualTo(5)); Assert.That(args.Get(parameter).Names[0], Is.EqualTo("a"));
            Assert.Throws<PlayServStrictResponseException>(() => method.Bind(Convert.ToBase64String(Encoding.UTF8.GetBytes("[{\"Count\":\"5\"}]"))));
            Assert.Throws<ArgumentException>(() => registry.Register<int>("duplicate", new[] { parameter, parameter }, (c, a, ct) => Task.FromResult(1)));
        }
        [UnityTest] public IEnumerator InvalidUtf8AndOversizeResultAreSafeFailures() => H.Run(async () =>
        {
            var parameter = new PlayServServerRpcParameter<string>("value");
            var registry = new PlayServServerRpcRegistry(); var calls = 0;
            registry.Register("sum", new[] { parameter }, (c, a, ct) =>
            { calls++; return Task.FromResult(new string('x', 1024 * 1024)); });
            await Configure(registry);
            Socket.Push("{\"type\":\"rpc_call\",\"id\":\"utf8\",\"method_name\":\"sum\",\"payload_base64\":\"/w==\"}");
            Call("huge", "[\"2026-09-14T00:00:00Z\"]");
            await Until(() => Replies().Length == 2);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(Replies().Single(s => s.Contains("\"id\":\"utf8\"")), Does.Contain("rpc_invalid_arguments"));
            Assert.That(Replies().Single(s => s.Contains("\"id\":\"huge\"")), Does.Contain("rpc_result_invalid"));
            Assert.That(Replies().All(s => Encoding.UTF8.GetByteCount(s) < 1024), Is.True);
        });
        [Test] public void BindingPreservesDecimalAndEscapedNestedJson()
        {
            var amount = new PlayServServerRpcParameter<decimal>("amount");
            var dto = new PlayServServerRpcParameter<RpcDto>("dto");
            var registry = new PlayServServerRpcRegistry();
            registry.Register<decimal>("exact", new PlayServServerRpcParameter[] { amount, dto }, (c, a, ct) => Task.FromResult(a.Get(amount)));
            var method = registry.Snapshot()["exact"];
            var json = "{\"amount\":1.0000000000000000001,\"dto\":{\"Count\":1,\"Names\":[\"a,b:c\",\"a\\\"b\"]}}";
            var args = method.Bind(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
            Assert.That(args.Get(amount), Is.EqualTo(1.0000000000000000001m));
            Assert.That(args.Get(dto).Names, Is.EqualTo(new[] { "a,b:c", "a\"b" }));
        }
        [UnityTest] public IEnumerator QueuedUnityCallbackDoesNotStartHandlerAfterDisconnect() => H.Run(async () =>
        {
            var registry = new PlayServServerRpcRegistry(); var calls = 0;
            registry.Register("sum", Array.Empty<PlayServServerRpcParameter>(), (c, a, ct) => { calls++; return Task.FromResult(1); });
            await Configure(registry);
            var callbacks = new ConcurrentQueue<Action>();
            PlayServGameServer.GetContextForServices().Dispatch = callbacks.Enqueue;
            Call("queued", "[]"); await Until(() => callbacks.Count > 0 && Uplink.PendingRpcCalls == 1);
            await Uplink.DisconnectAsync();
            while (callbacks.TryDequeue(out var callback)) callback();
            await Until(() => Uplink.PendingRpcCalls == 0);
            Assert.That(calls, Is.Zero); Assert.That(Replies(), Is.Empty);
        });
        [UnityEngine.Scripting.Preserve] public sealed class RpcDto
        {
            [UnityEngine.Scripting.Preserve] public RpcDto() { }
            [UnityEngine.Scripting.Preserve] public int Count;
            [UnityEngine.Scripting.Preserve] public List<string> Names;
        }
    }
}
