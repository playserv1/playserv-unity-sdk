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
using UnityEngine.TestTools;

namespace Playserv.Tests.Editor.GameServer
{
    public sealed class PlayServOperatorActionsEditorTests
    {
        private readonly List<PlayServGameServerUplink> _instances = new List<PlayServGameServerUplink>();
        private PlayServGameServerUplink _uplink;
        private ConcurrentQueue<Socket> _sockets;
        private ConcurrentQueue<string> _errors;
        private Socket Current => _sockets.Last();
        private static TaskCompletionSource<bool> Completion() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        [UnityTearDown] public IEnumerator Cleanup() => Run(async () =>
        {
            try { await Task.WhenAll(_instances.Select(uplink => uplink.DisconnectAsync())); }
            finally { _instances.Clear(); PlayServGameServer.CancelForModuleShutdown(); }
        });

        private async Task Configure(PlayServGameServerOptions options)
        {
            PlayServGameServer.CancelForModuleShutdown();
            options.BackendServerAddress = "https://api.playserv.test"; options.ExecutorSlug = "arena";
            options.ServerKeyProvider = new Key();
            PlayServGameServer.ConfigureForTesting(options, new Http(), () => DateTimeOffset.UtcNow,
                (_, ct) => Task.Delay(Timeout.Infinite, ct));
            _uplink = PlayServGameServer.Uplink; _instances.Add(_uplink);
            var sockets = _sockets = new ConcurrentQueue<Socket>();
            var errors = _errors = new ConcurrentQueue<string>();
            _uplink.OnError += error => errors.Enqueue(error.SourceCode + error.Message + error.RawDetails);
            _uplink.SocketFactory = () => { var socket = new Socket(); sockets.Enqueue(socket); return socket; };
            await _uplink.ConnectAsync();
        }

        private static string Frame(string type, string id = "request-one", string room = "one") =>
            "{\"type\":\"" + type + "\",\"request_id\":\"" + id + "\",\"room_name\":\"" + room + "\",\"player_id\":\"plr_one\"}";
        private static string[] Results(Socket socket) => socket.Sent.Where(s => s.Contains("_result\"")).ToArray();
        private async Task Barrier(Socket socket = null)
        {
            socket = socket ?? Current;
            var count = socket.Sent.Count(s => s == "{\"type\":\"pong\"}");
            socket.Push("{\"type\":\"ping\"}");
            await Until(() => socket.Sent.Count(s => s == "{\"type\":\"pong\"}") > count);
        }
        private Task Until(Func<bool> predicate) => WaitUntil(predicate, () => "state=" + _uplink.State + ", frames=" + Current.Sent.Count + ", errors=" + _errors.Count);

        [UnityTest] public IEnumerator CapabilitiesAreIndependentAndOptionsAreCaptured() => Run(async () =>
        {
            await Configure(new PlayServGameServerOptions());
            Assert.That(Current.Sent.First(), Does.Not.Contain("participant_remove").And.Not.Contain("room_close"));
            var options = new PlayServGameServerOptions { ParticipantRemoveHandler = (_, ct) => Task.FromResult(true) };
            await Configure(options);
            options.ParticipantRemoveHandler = (_, ct) => Task.FromResult(false);
            Assert.That(Current.Sent.First(), Does.Contain("participant_remove").And.Not.Contain("room_close").And.Not.Contain("room_create"));
            Current.Push(Frame("participant_remove"));
            await Until(() => Results(Current).Length == 1);
            Assert.That(Results(Current)[0], Is.EqualTo("{\"type\":\"participant_remove_result\",\"request_id\":\"request-one\",\"ok\":true}"));
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => Task.FromResult(true) });
            Assert.That(Current.Sent.First(), Does.Contain("room_close").And.Not.Contain("participant_remove"));
        });

        [UnityTest] public IEnumerator MissingTargetsStillInvokeGameHandlersOnUnityContextAndWait() => Run(async () =>
        {
            var done = Completion(); var invoked = false; var thread = Thread.CurrentThread.ManagedThreadId;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (request, ct) =>
            {
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(thread));
                Assert.That(request.RequestId, Is.EqualTo("request-one"));
                Assert.That(request.RoomName, Is.EqualTo("one"));
                Assert.That(request.Reason, Is.EqualTo("room_closed_by_operator"));
                invoked = true; return done.Task;
            } });
            var unity = SynchronizationContext.Current;
            PlayServGameServer.GetContextForServices().Dispatch = action => unity.Post(_ => action(), null);
            Current.Push(Frame("room_close"));
            await Until(() => invoked); await Barrier();
            Assert.That(Results(Current), Is.Empty);
            done.SetResult(true);
            await Until(() => Results(Current).Length == 1);
            Assert.That(Results(Current)[0], Is.EqualTo("{\"type\":\"room_close_result\",\"request_id\":\"request-one\",\"ok\":true}"));
        });

        [UnityTest] public IEnumerator FalseAndExceptionsHaveOnlySafeFailureReason() => Run(async () =>
        {
            foreach (var throws in new[] { false, true })
            {
                await Configure(new PlayServGameServerOptions { ParticipantRemoveHandler = (request, ct) =>
                {
                    Assert.That(request.PlayerId, Is.EqualTo("plr_one"));
                    Assert.That(request.Reason, Is.EqualTo("removed_by_operator"));
                    if (throws) throw new Exception("Bearer sk_private rsv_secret payload");
                    return Task.FromResult(false);
                } });
                Current.Push(Frame("participant_remove"));
                await Until(() => Results(Current).Length == 1);
                Assert.That(Results(Current)[0], Is.EqualTo("{\"type\":\"participant_remove_result\",\"request_id\":\"request-one\",\"ok\":false,\"reason\":\"operator_action_failed\"}"));
                Assert.That(string.Join("", Current.Sent.Concat(_errors)), Does.Not.Contain("sk_private").And.Not.Contain("rsv_secret").And.Not.Contain("payload"));
            }
        });

        [UnityTest] public IEnumerator DuplicateIdsDoNotOverlapAndConflictingActionsWait() => Run(async () =>
        {
            var done = Completion(); var calls = 0;
            await Configure(new PlayServGameServerOptions {
                ParticipantRemoveHandler = (_, ct) => { calls++; return done.Task; },
                RoomCloseHandler = (_, ct) => { calls++; return Task.FromResult(true); } });
            Current.Push(Frame("participant_remove", "first"));
            await Until(() => calls == 1);
            Current.Push(Frame("participant_remove", "first"));
            Current.Push(Frame("room_close", "second"));
            await Barrier(); Assert.That(calls, Is.EqualTo(1));
            done.SetResult(true);
            await Until(() => Results(Current).Length == 2);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(Results(Current)[0], Does.Contain("first"));
            Assert.That(Results(Current)[1], Does.Contain("second"));
        });

        [UnityTest] public IEnumerator BudgetIncludesQueuedUnityDispatchAndSuppressesLateCompletion() => Run(async () =>
        {
            var posted = new ConcurrentQueue<Action>(); var calls = 0;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => { calls++; return Task.FromResult(true); } });
            _uplink.OperatorTimeout = TimeSpan.FromMilliseconds(40);
            PlayServGameServer.GetContextForServices().Dispatch = action => posted.Enqueue(action);
            Current.Push(Frame("room_close")); await Barrier();
            await Until(() => posted.Count > 0);
            await Task.Delay(80);
            while (posted.TryDequeue(out var action)) action();
            await Until(() => _uplink.PendingOperatorActions == 0);
            Assert.That(calls, Is.Zero); Assert.That(Results(Current), Is.Empty);
        });

        [UnityTest] public IEnumerator TimedOutActiveHandlerKeepsItsLaneUntilItReallyFinishes() => Run(async () =>
        {
            var done = Completion(); var canceled = Completion(); var calls = 0;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) =>
            { calls++; ct.Register(() => canceled.TrySetResult(true)); return done.Task; } });
            _uplink.OperatorTimeout = TimeSpan.FromMilliseconds(60);
            Current.Push(Frame("room_close", "first")); await Until(() => calls == 1);
            await canceled.Task;
            Assert.That(_uplink.PendingOperatorActions, Is.EqualTo(1));
            Current.Push(Frame("room_close", "second")); await Barrier();
            Assert.That(calls, Is.EqualTo(1));
            await Task.Delay(80); done.SetResult(true);
            await Until(() => _uplink.PendingOperatorActions == 0);
            Assert.That(Results(Current), Is.Empty);
        });

        [UnityTest] public IEnumerator QueueCountAndByteLimitsRejectNewWorkWithoutLeakingPayload() => Run(async () =>
        {
            foreach (var bytes in new[] { false, true })
            {
                var done = Completion(); var called = 0;
                await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => { called++; return done.Task; } });
                if (bytes)
                {
                    var large = Frame("room_close", "first").TrimEnd('}') + ",\"ignored\":\"" + new string('x', 600000) + "\"}";
                    Current.Push(large); await Until(() => called == 1);
                    Current.Push(large.Replace("first", "second"));
                }
                else for (var i = 0; i < 65; i++) Current.Push(Frame("room_close", "id-" + i));
                await Barrier(); await Until(() => _errors.Any(e => e.Contains("operator_action_queue_full")));
                Assert.That(_uplink.PendingOperatorActions, Is.EqualTo(bytes ? 1 : 64));
                Assert.That(called, Is.EqualTo(1));
                await _uplink.DisconnectAsync(); done.SetResult(true);
                await Until(() => _uplink.PendingOperatorActions == 0);
            }
        });

        [UnityTest] public IEnumerator DisconnectCancelsWithoutWaitingAndOldCompletionCannotReplyOnReconnect() => Run(async () =>
        {
            var done = Completion(); var canceled = Completion(); var invoked = false;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) =>
            { invoked = true; ct.Register(() => canceled.TrySetResult(true)); return done.Task; } });
            Current.Push(Frame("room_close")); await Until(() => invoked);
            var old = Current; old.Abort();
            await canceled.Task;
            await Until(() => _sockets.Count == 2 && _uplink.State == PlayServUplinkState.Connected);
            done.SetResult(true); await Until(() => _uplink.PendingOperatorActions == 0); await Barrier();
            Assert.That(Results(old), Is.Empty); Assert.That(Results(Current), Is.Empty);
        });

        [UnityTest] public IEnumerator ReconfigurationAndShutdownCancelHandlersWithoutLateReplies() => Run(async () =>
        {
            var done = Completion(); var canceled = Completion(); var invoked = false;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) =>
            { invoked = true; ct.Register(() => canceled.TrySetResult(true)); return done.Task; } });
            var old = Current; var oldUplink = _uplink;
            old.Push(Frame("room_close")); await Until(() => invoked);
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => Task.FromResult(true) });
            await canceled.Task; done.SetResult(true);
            await WaitUntil(() => oldUplink.PendingOperatorActions == 0, () => "old pending=" + oldUplink.PendingOperatorActions);
            Assert.That(Results(old), Is.Empty); Assert.That(Results(Current), Is.Empty);
            done = Completion(); canceled = Completion(); invoked = false;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) =>
            { invoked = true; ct.Register(() => canceled.TrySetResult(true)); return done.Task; } });
            Current.Push(Frame("room_close")); await Until(() => invoked);
            await PlayServGameServer.ShutdownAsync(); await canceled.Task;
            done.SetResult(true); await Until(() => _uplink.PendingOperatorActions == 0);
            Assert.That(Results(Current), Is.Empty);
        });

        [UnityTest] public IEnumerator MalformedRequestsNeverInvokeTheHandler() => Run(async () =>
        {
            var called = 0;
            await Configure(new PlayServGameServerOptions { RoomCloseHandler = (_, ct) => { called++; return Task.FromResult(true); },
                ParticipantRemoveHandler = (_, ct) => { called++; return Task.FromResult(true); } });
            foreach (var frame in new[] { "{\"type\":\"room_close\"}", Frame("room_close").Replace("\"one\"", "123"),
                Frame("room_close").Replace("request-one", "unsafe\\nsecret"), Frame("room_close").TrimEnd('}') + ",\"room_name\":\"two\"}",
                Frame("room_close").Replace("request-one", "request-one\\n"),
                Frame("room_close").Replace("\"one\"", "\"one\\n\""),
                Frame("participant_remove").Replace("plr_one", "plr_one\\n") }) Current.Push(frame);
            await Barrier(); Assert.That(called, Is.Zero); Assert.That(Results(Current), Is.Empty);
            Assert.That(_errors.Count, Is.EqualTo(7));
            Assert.That(string.Join("", _errors), Does.Not.Contain("secret"));
        });

        private static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var until = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted) { Assert.That(DateTime.UtcNow, Is.LessThan(until), "Operator fixture timed out."); yield return null; }
            task.GetAwaiter().GetResult();
        }
        private static async Task WaitUntil(Func<bool> predicate, Func<string> diagnostics)
        {
            for (var i = 0; i < 500; i++) { if (predicate()) return; await Task.Delay(10); }
            Assert.Fail("Operator state not reached: " + diagnostics());
        }
        private sealed class Key : IPlayServServerKeyProvider
        { public Task<string> GetServerKeyAsync(CancellationToken ct = default) => Task.FromResult("sk_operator_fixture"); }
        private sealed class Http : IPlayServGameServerTransport
        {
            public Task<PlayServGameServerHttpResponse> SendAsync(PlayServGameServerHttpRequest request, CancellationToken ct) =>
                Task.FromResult(new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{}" });
        }
        private sealed class Socket : IPlayServUplinkSocket
        {
            private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
            private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
            private readonly CancellationTokenSource _closed = new CancellationTokenSource();
            internal readonly ConcurrentQueue<string> Sent = new ConcurrentQueue<string>();
            internal Socket() { Push("{\"type\":\"uplink_hello_ack\",\"session_token\":\"fixture-session\",\"expires_in\":3600,\"admission\":\"push\",\"authority\":{\"enabled\":false,\"reason\":\"not_enabled\"},\"room_config\":null}"); }
            public Task ConnectAsync(Uri endpoint, string credential, CancellationToken ct) => Task.CompletedTask;
            public Task SendAsync(byte[] bytes, CancellationToken ct) { ct.ThrowIfCancellationRequested(); _closed.Token.ThrowIfCancellationRequested(); Sent.Enqueue(Encoding.UTF8.GetString(bytes)); return Task.CompletedTask; }
            internal void Push(string json) { _incoming.Enqueue(json); _available.Release(); }
            public async Task<string> ReceiveAsync(CancellationToken ct)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closed.Token);
                await _available.WaitAsync(linked.Token).ConfigureAwait(false); _incoming.TryDequeue(out var json); return json;
            }
            public void Abort() => _closed.Cancel();
            public void Dispose() { }
        }
    }
}
