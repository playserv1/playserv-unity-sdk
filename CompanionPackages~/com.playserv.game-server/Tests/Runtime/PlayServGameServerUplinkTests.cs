using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServGameServerUplinkTests
    {
        internal const string Config = "{\"capacity\":8,\"reservation_ttl_seconds\":10,\"room_lifetime_seconds\":120,\"room_idle_timeout_seconds\":30,\"max_rooms\":2,\"version\":1}";
        internal static string Ack(string token = "session-one", string config = Config, int ttl = 3600) =>
            "{\"type\":\"uplink_hello_ack\",\"session_token\":\"" + token + "\",\"expires_in\":" + ttl + ",\"admission\":\"consume\",\"room_config\":" + config + "}";
        private Http _http;
        private ConcurrentQueue<Socket> _sockets;
        private double _seconds;
        private Key _key;

        [SetUp] public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown();
            _seconds = 100;
            _http = new Http(); _sockets = new ConcurrentQueue<Socket>(); _key = new Key();
            Configure();
        }
        private void Configure(string ack = null, IPlayServUplinkCredentialProvider credential = null)
        {
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", InstanceId = "unity-test",
                ServerKeyProvider = _key, UplinkCredentialProvider = credential
            }, _http, () => DateTimeOffset.FromUnixTimeMilliseconds(1000), (_, ct) => Task.Delay(Timeout.Infinite, ct));
            PlayServGameServer.GetContextForServices().MonotonicSeconds = () => _seconds;
            PlayServGameServer.Uplink.Seconds = () => _seconds;
            PlayServGameServer.Uplink.SocketFactory = () =>
            {
                var socket = new Socket(ack ?? Ack()); _sockets.Enqueue(socket); return socket;
            };
        }
        [TearDown] public void Teardown() => PlayServGameServer.CancelForModuleShutdown();

        [UnityTest] public IEnumerator HelloIsSecretFree_UpgradeAndRestUseDifferentCredentials() => Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var socket = _sockets.Single();
            Assert.That(socket.Endpoint.AbsoluteUri, Is.EqualTo("wss://api.playserv.test/uplink"));
            Assert.That(socket.Credential, Is.EqualTo("sk_uplink_test"));
            var hello = socket.Sent.Single();
            Assert.That(hello, Does.Contain("\"instance_id\":\"unity-test\""));
            Assert.That(hello, Does.Contain("\"capabilities\":[\"admission_push\"]"));
            Assert.That(hello, Does.Not.Contain("sk_").And.Not.Contain("Authorization"));
            await PlayServGameServer.ListRoomsAsync("arena");
            Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("session-one"));
            Assert.That(await PlayServGameServer.ResolveServerKeyForRealtimeAsync(PlayServGameServer.GetContextForServices(), default), Is.EqualTo("sk_uplink_test"));
        });

        [UnityTest] public IEnumerator LifecycleEventsAreOrderedAndConfigurationDoesNotRepeat() => Run(async () =>
        {
            var states = new List<PlayServUplinkState>(); var versions = new List<long>();
            var uplink = PlayServGameServer.Uplink; var thread = Thread.CurrentThread.ManagedThreadId;
            var delivery = SynchronizationContext.Current;
            Assert.That(delivery, Is.Not.Null, "This fixture must run with Unity's delivery context.");
            PlayServGameServer.GetContextForServices().Dispatch = action => delivery.Post(_ => action(), null);
            uplink.StateChanged += state => { Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(thread)); states.Add(state); };
            uplink.ConfigurationChanged += value => versions.Add(value?.Version ?? -1);
            await uplink.ConnectAsync();
            await Until(() => states.Count >= 2 && versions.Count == 1);
            Assert.That(states.Take(2), Is.EqualTo(new[] { PlayServUplinkState.Connecting, PlayServUplinkState.Connected }));
            uplink.ApplyConfiguration(new RoomConfigWire { capacity = 8, reservation_ttl_seconds = 10, max_rooms = 2, version = 1 });
            uplink.ApplyConfiguration(new RoomConfigWire { capacity = 8, reservation_ttl_seconds = 10, max_rooms = 2, version = 2 });
            await Until(() => versions.Count == 2);
            Assert.That(versions, Is.EqualTo(new long[] { 1, 2 }));
            await uplink.DisconnectAsync(); await Until(() => states.Last() == PlayServUplinkState.Disconnected && versions.Last() == -1);
        });

        [UnityTest] public IEnumerator FirstRoomBootstraps_ConfigurationMetadataAndSharedConnection() => Run(async () =>
        {
            var one = await PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest("arena",
                new PlayServGameRoomSnapshot("one", 0, 16, connect: new PlayServGameRoomConnect("game.test", 7777, "udp"), region: "eu")));
            var two = await Start("two");
            Assert.That(_sockets.Count, Is.EqualTo(1));
            var body = _http.Requests.First().JsonBody;
            Assert.That(body, Does.Contain("\"capacity\":8").And.Contain("\"instance_id\":\"unity-test\""));
            Assert.That(body, Does.Contain("\"region\":\"eu\"").And.Contain("\"host\":\"game.test\""));
            Assert.That(one.Configuration.ReservationTtlSeconds, Is.EqualTo(10));
            await one.CloseAsync(); await two.CloseAsync();
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Connected));
            var shutdown = await PlayServGameServer.ShutdownAsync();
            Assert.That(shutdown.IsSuccess, Is.True);
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Disconnected));
            await PlayServGameServer.ShutdownAsync();
        });

        [UnityTest] public IEnumerator MissingConfigRefusesRoomButKeepsUplink() => Run(async () =>
        {
            PlayServGameServer.CancelForModuleShutdown(); Configure(Ack(config: "null"));
            var error = await Error(() => Start("one"));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("room_configuration_missing"));
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Connected));
        });

        [UnityTest] public IEnumerator PresenceIsExplicit_DeduplicatedAndRepairedOnlyOnMismatch() => Run(async () =>
        {
            var room = await Start("one");
            var socket = _sockets.Single();
            room.ReportJoin("plr_01J8ZKQ4X0"); room.ReportJoin("plr_01J8ZKQ4X0"); room.ReportJoin("bot", true);
            await room.DrainPresenceForTesting();
            Assert.That(socket.Sent.Count(s => s.Contains("\"event\":\"join\"")), Is.EqualTo(1));
            await room.HeartbeatAsync();
            Assert.That(_http.Requests.Last().JsonBody, Does.Contain("761b2b3a0187ba8698d89cb10312305c5c1951800b94b6191fb8a613dc5f88d2"));
            var rosters = socket.Sent.Count(s => s.Contains("\"event\":\"roster\""));
            await room.HeartbeatAsync();
            Assert.That(socket.Sent.Count(s => s.Contains("\"event\":\"roster\"")), Is.EqualTo(rosters));
            _http.RosterCheck = "mismatch";
            var divergences = 0; room.PresenceDiverged += _ => divergences++;
            await room.HeartbeatAsync(); await room.DrainPresenceForTesting();
            await room.HeartbeatAsync(); await room.DrainPresenceForTesting();
            await room.HeartbeatAsync(); await room.DrainPresenceForTesting();
            await room.HeartbeatAsync(); await room.DrainPresenceForTesting();
            Assert.That(divergences, Is.EqualTo(1));
            room.Kick("plr_01J8ZKQ4X0"); await room.DrainPresenceForTesting();
            Assert.That(socket.Sent.Last(), Does.Contain("\"event\":\"leave\""));
        });

        [Test] public void CanonicalRosterHashMatchesContractVectors()
        {
            Assert.That(PlayServGameRoomHandle.ComputeRosterHash(Array.Empty<string>()), Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
            Assert.That(PlayServGameRoomHandle.ComputeRosterHash(new[] { "plr_01J8ZKQ4X2", "plr_01J8ZKQ4X0", "plr_01J8ZKQ4X1", "plr_01J8ZKQ4X0" }),
                Is.EqualTo("26741b065f492be34e5fe44755321f11313291699161950ee0ba800ef2d2c57e"));
        }

        [UnityTest] public IEnumerator ConfigurationVersionQuotaAndIdleAreAuthoritative() => Run(async () =>
        {
            var room = await Start("one"); await Start("two");
            Assert.That((await Error(() => Start("three"))).UnifiedError.SourceCode, Is.EqualTo("room_quota_exceeded"));
            _http.Config = Config.Replace("\"capacity\":8", "\"capacity\":1").Replace("\"version\":1", "\"version\":2");
            room.Update(new PlayServGameRoomSnapshot("one", 2, 8));
            await room.HeartbeatAsync(); await room.HeartbeatAsync();
            Assert.That(_http.Requests.Last().JsonBody, Does.Contain("\"players\":2").And.Contain("\"capacity\":1").And.Contain("\"open\":false"));
            _http.Config = Config; await room.HeartbeatAsync();
            Assert.That(room.Configuration.Version, Is.EqualTo(2));
            room.ReportJoin("plr_human"); _seconds += 31; await room.EvaluateLifetimeAsync();
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Active));
            room.ReportLeave("plr_human"); _seconds += 31; await room.EvaluateLifetimeAsync();
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Closed));
        });

        [UnityTest] public IEnumerator ReconnectRotatesCredentialAndRepairsRoster() => Run(async () =>
        {
            var room = await Start("one"); room.ReportJoin("plr_one"); await room.DrainPresenceForTesting();
            _key.Value = "sk_rotated";
            _sockets.Single().Abort();
            await Until(() => _sockets.Count == 2 && PlayServGameServer.Uplink.State == PlayServUplinkState.Connected);
            await Until(() => _sockets.Last().Sent.Any(s => s.Contains("\"event\":\"roster\"")));
            Assert.That(_sockets.Last().Credential, Is.EqualTo("sk_rotated"));
            Assert.That(_sockets.Last().Sent.First(), Does.Contain("\"instance_id\":\"unity-test\"").And.Contain("\"resume\":true"));
        });

        [UnityTest] public IEnumerator RenewalAndMissingRenewalNeverSendExpiredBearer() => Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            _sockets.Single().Push("{\"type\":\"session_token\",\"session_token\":\"session-two\",\"expires_in\":2}");
            await Until(async () => await PlayServGameServer.Uplink.GetRestCredentialAsync(PlayServGameServer.GetContextForServices(), default) == "session-two");
            await PlayServGameServer.ListRoomsAsync("arena");
            Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("session-two"));
            _seconds += 3;
            await Until(() => _sockets.Count == 2 && PlayServGameServer.Uplink.State == PlayServUplinkState.Connected);
            await PlayServGameServer.ListRoomsAsync("arena");
            Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("session-one"));
        });

        [UnityTest] public IEnumerator PongRefusalAndSafeDiagnostics() => Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var socket = _sockets.Single(); var errors = new ConcurrentQueue<string>();
            PlayServGameServer.Uplink.OnError += e => errors.Enqueue(e.SourceCode + e.Message + e.RawDetails);
            socket.Push("{\"type\":\"ping\"}");
            await Until(() => socket.Sent.Contains("{\"type\":\"pong\"}"));
            socket.Push("{\"type\":\"rpc_call\",\"payload\":\"Bearer sk_secret a.b.c\"}");
            socket.Push("{\"type\":\"rpc_call\"}");
            await Until(() => errors.Count == 1);
            socket.Fail(new PlayServUplinkFailure("instance_id_conflict", true));
            await Until(() => PlayServGameServer.Uplink.State == PlayServUplinkState.Terminated);
            Assert.That(errors.All(s => !s.Contains("sk_") && !s.Contains("a.b.c")), Is.True);
            Assert.That((await Error(() => PlayServGameServer.Uplink.ConnectAsync())).UnifiedError.SourceCode, Is.EqualTo("instance_id_conflict"));
        });

        [UnityTest] public IEnumerator LimitsAndInvalidFramesAreFailClosed() => Run(async () =>
        {
            await PlayServGameServer.Uplink.ConnectAsync();
            var error = await Error(() => PlayServGameServer.Uplink.SendAsync(new { payload = new string('x', 1024 * 1024) }));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("frame_too_large"));
            _sockets.Single().Push("not JSON sk_secret");
            await Until(() => PlayServGameServer.Uplink.State == PlayServUplinkState.Terminated);
            Assert.That(PlayServGameServer.Uplink.LastError.SourceCode, Is.EqualTo("uplink_invalid_frame"));
        });

        [UnityTest] public IEnumerator PreCancellationAndWrongExecutorDoNotSendRequests() => Run(async () =>
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await PlayServGameServer.Uplink.ConnectAsync(canceled.Token); Assert.Fail(); } catch (OperationCanceledException) { }
            Assert.That(_sockets, Is.Empty);
            try { await PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest("other", new PlayServGameRoomSnapshot("one", 0, 8))); Assert.Fail(); }
            catch (InvalidOperationException) { }
            Assert.That(_http.Requests, Is.Empty);
            await Task.WhenAll(PlayServGameServer.Uplink.ConnectAsync(), PlayServGameServer.Uplink.ConnectAsync());
            Assert.That(_sockets.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator DeploymentCredentialAccepted_PlayerAndClientCredentialsRejected() => Run(async () =>
        {
            foreach (var invalid in new[] { "pk_client", "a.b.c", "sk_bad\nsecret" })
            {
                PlayServGameServer.CancelForModuleShutdown(); Configure(credential: new Credential(invalid));
                Assert.That((await Error(() => PlayServGameServer.Uplink.ConnectAsync())).UnifiedError.SourceCode, Is.EqualTo("uplink_invalid_credential"));
            }
            Assert.That(_sockets, Is.Empty);
            PlayServGameServer.CancelForModuleShutdown();
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"executor/x\",\"deployment_id\":\"dep_x\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var token = "a." + payload + ".c";
            Configure(credential: new Credential(token));
            await PlayServGameServer.Uplink.ConnectAsync();
            Assert.That(_sockets.Single().Credential, Is.EqualTo(token));
        });

        [UnityTest] public IEnumerator CancelOneConnectWaiterDoesNotCancelTheSharedSession() => Run(async () =>
        {
            var socket = new Socket(null); PlayServGameServer.Uplink.SocketFactory = () => socket;
            using var cancel = new CancellationTokenSource();
            var first = PlayServGameServer.Uplink.ConnectAsync(cancel.Token);
            var second = PlayServGameServer.Uplink.ConnectAsync();
            await Until(() => socket.Sent.Count == 1);
            cancel.Cancel();
            try { await first; Assert.Fail(); } catch (OperationCanceledException) { }
            socket.Push(Ack()); await second;
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Connected));
            Assert.That(socket.Sent.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator EveryTerminalHandshakeReasonStopsRetry() => Run(async () =>
        {
            foreach (var reason in new[] { "uplink_unauthorized", "instance_id_missing", "instance_id_conflict", "protocol_unsupported", "executor_not_found" })
            {
                PlayServGameServer.CancelForModuleShutdown(); Configure();
                var socket = new Socket(null); socket.Fail(new PlayServUplinkFailure(reason, true));
                PlayServGameServer.Uplink.SocketFactory = () => socket;
                var error = await Error(() => PlayServGameServer.Uplink.ConnectAsync());
                Assert.That(error.UnifiedError.SourceCode, Is.EqualTo(reason));
                Assert.That(error.UnifiedError.Retryable, Is.False);
                Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Terminated));
            }
        });

        [UnityTest] public IEnumerator FragmentedReceiveHasAnAggregateByteLimit() => Run(async () =>
        {
            using (var wire = new PlayServUplinkSocket(new Fragments(1024 * 1024)))
                Assert.That((await wire.ReceiveAsync(default)).Length, Is.EqualTo(1024 * 1024));
            var oversized = new Fragments(1024 * 1024 + 1);
            using (var wire = new PlayServUplinkSocket(oversized))
            {
                try { await wire.ReceiveAsync(default); Assert.Fail(); }
                catch (PlayServUplinkFailure ex) { Assert.That(ex.Code, Is.EqualTo("frame_too_large")); }
                Assert.That(oversized.Aborted, Is.True);
            }
        });

        [UnityTest] public IEnumerator SilenceTriggersReconnectButNotRoomTermination() => Run(async () =>
        {
            var room = await Start("one"); _seconds += 16;
            await Until(() => _sockets.Count == 2 && PlayServGameServer.Uplink.State == PlayServUplinkState.Connected);
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Active));
        });

        [UnityTest] public IEnumerator ShutdownDoesNotWaitForAnUncooperativeCredentialProvider() => Run(async () =>
        {
            PlayServGameServer.CancelForModuleShutdown();
            var provider = new PendingCredential(); Configure(credential: provider);
            var connect = PlayServGameServer.Uplink.ConnectAsync();
            await Until(() => provider.Called);
            await PlayServGameServer.ShutdownAsync();
            try { await connect; Assert.Fail(); } catch (OperationCanceledException) { }
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Disconnected));
            provider.Result.SetException(new Exception("sk_secret must not reach a sink"));
        });

        internal static Task<PlayServGameRoomHandle> Start(string room) => PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest("arena", new PlayServGameRoomSnapshot(room, 0, 8)));
        internal static async Task<PlayServGameServerException> Error(Func<Task> call)
        {
            try { await call(); } catch (PlayServGameServerException ex) { return ex; }
            Assert.Fail("Expected a typed server error."); return null;
        }
        internal static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var started = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(15)), "Test timed out.");
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }
        internal static Task Until(Func<bool> predicate, Func<string> diagnostics = null) =>
            Until(() => Task.FromResult(predicate()), diagnostics);
        internal static async Task Until(Func<Task<bool>> predicate, Func<string> diagnostics = null)
        {
            for (var i = 0; i < 500; i++) { if (await predicate()) return; await Task.Delay(10); }
            Assert.Fail("Expected state was not reached." + (diagnostics == null ? "" : " " + diagnostics()));
        }
        // Admission fixtures can replace the static facade while its old receive loop is still stopping.
        // Keep ownership of every instance until UnityTearDown has awaited its completion.
        internal sealed class AdmissionTestScope
        {
            private readonly List<PlayServGameServerUplink> _uplinks = new List<PlayServGameServerUplink>();
            private readonly List<PlayServGameRoomHandle> _rooms = new List<PlayServGameRoomHandle>();
            private readonly List<Action> _unsubscribe = new List<Action>();

            internal void Track(PlayServGameServerUplink uplink) => _uplinks.Add(uplink);
            internal void Track(PlayServGameRoomHandle room) => _rooms.Add(room);
            internal void UnsubscribeOnCleanup(Action unsubscribe) => _unsubscribe.Add(unsubscribe);

            internal async Task CleanupAsync()
            {
                try
                {
                    await Task.WhenAll(_uplinks.Select(uplink => uplink.DisconnectAsync()));
                    await Task.WhenAll(_rooms.Select(room => room.DrainPresenceForTesting()));
                }
                finally
                {
                    try { foreach (var unsubscribe in _unsubscribe) unsubscribe(); }
                    finally
                    {
                        _unsubscribe.Clear(); _rooms.Clear(); _uplinks.Clear();
                        PlayServGameServer.CancelForModuleShutdown();
                    }
                }
            }
        }
        internal sealed class Key : IPlayServServerKeyProvider
        {
            public string Value = "sk_uplink_test";
            public Task<string> GetServerKeyAsync(CancellationToken ct = default) => Task.FromResult(Value);
        }
        internal sealed class Credential : IPlayServUplinkCredentialProvider
        {
            private readonly string _value;
            internal Credential(string value) { _value = value; }
            public Task<string> GetCredentialAsync(CancellationToken ct = default) => Task.FromResult(_value);
        }
        private sealed class PendingCredential : IPlayServUplinkCredentialProvider
        {
            internal volatile bool Called;
            internal readonly TaskCompletionSource<string> Result = new TaskCompletionSource<string>();
            public Task<string> GetCredentialAsync(CancellationToken ct = default) { Called = true; return Result.Task; }
        }
        internal sealed class Socket : IPlayServUplinkSocket
        {
            private readonly ConcurrentQueue<object> _incoming = new ConcurrentQueue<object>();
            private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
            private readonly CancellationTokenSource _closed = new CancellationTokenSource();
            public readonly ConcurrentQueue<string> Sent = new ConcurrentQueue<string>();
            public string Credential; public Uri Endpoint;
            internal Socket(string ack) { if (ack != null) Push(ack); }
            public Task ConnectAsync(Uri endpoint, string credential, CancellationToken ct) { Endpoint = endpoint; Credential = credential; return Task.CompletedTask; }
            public Task SendAsync(byte[] bytes, CancellationToken ct) { ct.ThrowIfCancellationRequested(); _closed.Token.ThrowIfCancellationRequested(); Sent.Enqueue(Encoding.UTF8.GetString(bytes)); return Task.CompletedTask; }
            internal void Push(string json) { _incoming.Enqueue(json); _available.Release(); }
            internal void Fail(Exception error) { _incoming.Enqueue(error); _available.Release(); }
            public async Task<string> ReceiveAsync(CancellationToken ct)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closed.Token);
                await _available.WaitAsync(linked.Token).ConfigureAwait(false);
                _incoming.TryDequeue(out var value);
                if (value is Exception error) throw error;
                return (string)value;
            }
            public void Abort() => _closed.Cancel();
            public void Dispose() { }
        }
        internal sealed class Http : IPlayServGameServerTransport
        {
            public Func<PlayServGameServerHttpRequest, CancellationToken, Task<PlayServGameServerHttpResponse>> Handler;
            public string Config = PlayServGameServerUplinkTests.Config, RosterCheck = "ok";
            public readonly ConcurrentQueue<PlayServGameServerHttpRequest> Requests = new ConcurrentQueue<PlayServGameServerHttpRequest>();
            public Task<PlayServGameServerHttpResponse> SendAsync(PlayServGameServerHttpRequest request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); Requests.Enqueue(request);
                if (Handler != null) return Handler(request, ct);
                return Task.FromResult(new PlayServGameServerHttpResponse { StatusCode = 200,
                    Body = request.RelativePath.EndsWith("upsert") ? "{\"created\":true,\"placement\":{\"state\":\"active\",\"open\":true},\"room_config\":" + Config + ",\"roster_check\":\"" + RosterCheck + "\"}" : request.Method == "GET" ? "[]" : "{}" });
            }
        }
        private sealed class Fragments : WebSocket
        {
            private int _remaining;
            internal bool Aborted;
            internal Fragments(int length) { _remaining = length; }
            public override WebSocketCloseStatus? CloseStatus => null;
            public override string CloseStatusDescription => null;
            public override string SubProtocol => null;
            public override WebSocketState State => Aborted ? WebSocketState.Aborted : WebSocketState.Open;
            public override void Abort() { Aborted = true; }
            public override void Dispose() { }
            public override Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken ct) => Task.CompletedTask;
            public override Task CloseOutputAsync(WebSocketCloseStatus status, string reason, CancellationToken ct) => Task.CompletedTask;
            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) => Task.CompletedTask;
            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            {
                var count = Math.Min(buffer.Count, _remaining);
                for (var i = 0; i < count; i++) buffer.Array[buffer.Offset + i] = (byte)'x';
                _remaining -= count;
                return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, _remaining == 0));
            }
        }
    }
}
