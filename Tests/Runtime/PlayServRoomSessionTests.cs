using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Playserv.Matchmaking;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServRoomSessionTests
    {
        private static IEnumerator Run(Func<Task> check)
        { var task = check(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        private static async Task Until(Func<bool> condition)
        {
            var start = DateTime.UtcNow;
            while (!condition()) { if (DateTime.UtcNow - start > TimeSpan.FromSeconds(3)) Assert.Fail("Fixture condition timed out"); await Task.Yield(); }
        }
        [UnityTest]
        public IEnumerator Host_passes_reservation_directly_and_waits_for_admission_and_snapshot() => Run(async () =>
        {
            var f = new Fixture(); using var session = f.Create();
            var enter = session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
            await Until(() => f.Sockets.Count == 1 && f.Sockets[0].Sent.Count == 1);
            Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Entering));
            Assert.That(enter.IsCompleted, Is.False);
            Assert.That(f.Hosts, Is.EqualTo(1)); Assert.That(f.Joins, Is.EqualTo(0));
            f.Sockets[0].Receive("admitted:code:player");
            await Task.Yield(); Assert.That(enter.IsCompleted, Is.False);
            f.Sockets[0].Receive("snapshot");
            Assert.That((await enter).RoomName, Is.EqualTo("code"));
            Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Ready));
            await session.SendTextAsync("input"); Assert.That(f.Sockets[0].Sent, Does.Contain("input"));
            await session.LeaveAsync(); Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Closed));
            Assert.That(f.Sockets[0].Sent, Does.Contain("leave")); Assert.That(f.Sockets[0].Disposed, Is.True);
        });
        [UnityTest]
        public IEnumerator Wrong_room_or_player_fails_admission() => Run(async () =>
        {
            foreach (var admission in new[] { "admitted:other:player", "admitted:code:other" })
            {
                var f = new Fixture(); using var session = f.Create();
                var enter = session.JoinAsync(new PlayServJoinRoomRequest { FunctionSlug = "room", RoomName = "code" });
                await Until(() => f.Sockets.Count > 0 && f.Sockets[0].Sent.Count > 0);
                f.Sockets[0].Receive(admission);
                await Failure(enter);
                Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Failed));
                Assert.That(f.Sockets[0].Disposed, Is.True);
            }
        });
        [UnityTest]
        public IEnumerator Handshake_without_snapshot_times_out_and_missing_endpoint_never_opens_socket() => Run(async () =>
        {
            var f = new Fixture(); using var session = f.Create(new PlayServRoomSessionOptions { EntryTimeout = TimeSpan.FromMilliseconds(80) });
            await Failure(session.ConnectAsync("room", Reservation("ticket")));
            Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Failed));
            var g = new Fixture(); using var other = g.Create();
            await Failure(other.ConnectAsync("room", new PlayServMatchReservation("code", "ticket", DateTimeOffset.MaxValue)));
            Assert.That(g.Sockets, Is.Empty);
        });
        [UnityTest]
        public IEnumerator Double_click_and_leave_discard_pending_host_without_retry() => Run(async () =>
        {
            var f = new Fixture(); var pending = new TaskCompletionSource<PlayServMatchResult>(); f.Host = ct => pending.Task;
            using var session = f.Create();
            var enter = session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
            await Failure(session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" }));
            Assert.That(f.Hosts, Is.EqualTo(1));
            await session.LeaveAsync(); await Failure(enter);
            pending.SetResult(Matched("late")); await Task.Yield();
            Assert.That(f.Sockets, Is.Empty); Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Closed));
        });
        [UnityTest]
        public IEnumerator Recovery_uses_new_join_ticket_connector_and_protocol_and_ignores_old_callbacks() => Run(async () =>
        {
            var f = new Fixture { AutoReady = true }; using var session = f.Create();
            await session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
            f.Sockets[0].RemoteClose();
            await Until(() => f.Sockets.Count == 2 && session.State == PlayServRoomSessionState.Ready);
            Assert.That(f.Hosts, Is.EqualTo(1)); Assert.That(f.Joins, Is.EqualTo(1)); Assert.That(f.Protocols.Count, Is.EqualTo(2));
            Assert.That(f.Sockets[0].Sent[0], Does.Contain("host-ticket"));
            Assert.That(f.Sockets[1].Sent[0], Does.Contain("join-1"));
            f.Sockets[0].ReceiveLate("admitted:wrong:player"); f.Sockets[0].RemoteClose(); await Task.Yield();
            Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Ready));
            Assert.That(f.Protocols[0].Disposed, Is.True);
        });
        [UnityTest]
        public IEnumerator Recovery_honors_retry_after_and_stops_after_three_attempts() => Run(async () =>
        {
            var f = new Fixture { AutoReady = true };
            f.Join = ct => Task.FromException<PlayServMatchResult>(new PlayServMatchmakingException(PlayServMatchmakingOperation.JoinRoom, "room", new PlayServError(PlayServErrorCode.Network, "room_unreachable", "Unavailable", retryable: true)) { RetryAfter = TimeSpan.FromSeconds(7) });
            using var session = f.Create(); await session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
            f.Sockets[0].RemoteClose();
            await Until(() => session.State == PlayServRoomSessionState.Failed);
            Assert.That(f.Joins, Is.EqualTo(3)); Assert.That(f.Delays, Does.Contain(TimeSpan.FromSeconds(7)));
        });
        [UnityTest]
        public IEnumerator Leave_cancels_recovery_and_terminal_refusal_stops_immediately() => Run(async () =>
        {
            foreach (var terminal in new[] { false, true })
            {
                var f = new Fixture { AutoReady = true }; var pending = new TaskCompletionSource<PlayServMatchResult>();
                f.Join = ct => terminal ? Task.FromException<PlayServMatchResult>(new PlayServRoomEntryRefusedException("room_refused")) : pending.Task;
                using var session = f.Create(); await session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
                f.Sockets[0].RemoteClose(); await Until(() => f.Joins == 1);
                if (terminal) await Until(() => session.State == PlayServRoomSessionState.Failed);
                else { await session.LeaveAsync(); pending.SetResult(Matched("late")); await Task.Yield(); Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Closed)); }
                Assert.That(f.Joins, Is.EqualTo(1)); Assert.That(f.Sockets.Count, Is.EqualTo(1));
            }
        });
        [UnityTest]
        public IEnumerator Late_protocol_rejection_during_leave_keeps_Closed_and_preserves_first_refusal() => Run(async () =>
        {
            var f = new Fixture { AutoReady = true }; using var session = f.Create();
            await session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
            f.Protocols[0].ExitWait = new TaskCompletionSource<bool>();
            var leave = session.LeaveAsync();
            f.Protocols[0].Context.Reject(new PlayServError(PlayServErrorCode.Forbidden, "late_refusal", "Refused"));
            Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Closed));
            f.Protocols[0].ExitWait.SetResult(true); await leave;
            var g = new Fixture(); using var other = g.Create();
            var entry = other.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
            await Until(() => g.Protocols.Count > 0);
            g.Protocols[0].Context.Reject(new PlayServError(PlayServErrorCode.Forbidden, "actual_refusal", "Refused"));
            await Failure(entry);
            Assert.That(other.LastError.SourceCode, Is.EqualTo("actual_refusal"));
        });
        [UnityTest]
        public IEnumerator Clearing_managed_identity_blocks_old_room_sends_before_settings_are_updated() => Run(async () =>
        {
            var f = new Fixture { AutoReady = true };
            f.Settings.PlayerAccessToken = null; f.Settings.BackendServerAddress = "https://fixture.example";
            using var managed = new PlayServPlayerSession(f.Settings, new PlayServBrowserOAuthTests.Http(), new Store());
            await managed.GetTokenAsync();
            f.Settings.PlayerId = managed.PlayerId; f.Settings.RuntimeTokenProvider = managed;
            using var session = f.Create(); await session.HostAsync(new PlayServHostRoomRequest { FunctionSlug = "room" });
            await managed.InvalidateAsync();
            await Failure(session.SendTextAsync("input-after-logout"));
            Assert.That(f.Sockets[0].Sent, Does.Not.Contain("input-after-logout"));
            Assert.That(session.State, Is.EqualTo(PlayServRoomSessionState.Failed));
        });
        private sealed class Store : IPlayServPlayerSessionStore
        {
            public Task<PlayServPlayerSessionData> LoadAsync(string key, CancellationToken ct = default) => Task.FromResult<PlayServPlayerSessionData>(null);
            public Task SaveAsync(string key, PlayServPlayerSessionData value, CancellationToken ct = default) => Task.CompletedTask;
            public Task ClearAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        }
        private static async Task Failure(Task task)
        { try { await task; Assert.Fail("Expected failure"); } catch (AssertionException) { throw; } catch { } }
        internal static PlayServMatchReservation Reservation(string ticket) => new PlayServMatchReservation("code", ticket, DateTimeOffset.MaxValue,
            new PlayServRoomConnect("localhost", 443, "wss", "wss://localhost/game", null), null, null, 60, () => 0, 0);
        private static PlayServMatchResult Matched(string ticket) => new PlayServMatchResult(PlayServMatchStatus.Matched, Reservation(ticket), null);
        private sealed class Fixture
        {
            public readonly List<Socket> Sockets = new List<Socket>(); public readonly List<Protocol> Protocols = new List<Protocol>();
            public readonly List<TimeSpan> Delays = new List<TimeSpan>();
            public int Hosts, Joins; public bool AutoReady;
            public Func<CancellationToken,Task<PlayServMatchResult>> Host, Join;
            public readonly PlayServSettings Settings = new PlayServSettings { ClientToken = "pk_fixture", PlayerId = "player", PlayerAccessToken = "fixture-token" };
            public PlayServRoomSession Create(PlayServRoomSessionOptions options = null) => new PlayServRoomSession(
                () => { var p = new Protocol(); Protocols.Add(p); return p; }, options,
                (request, ct) => { Hosts++; return Host?.Invoke(ct) ?? Task.FromResult(Matched("host-ticket")); },
                (request, ct) => { Assert.That(request.RoomName, Is.EqualTo("code")); Joins++; return Join?.Invoke(ct) ?? Task.FromResult(Matched("join-" + Joins)); },
                config => new PlayServGameConnection(config, () => Settings, ctx =>
                {
                    var socket = new Socket { AutoReady = AutoReady, Player = Settings.PlayerId }; Sockets.Add(socket); return socket;
                }, new NewtonsoftJsonCodec(), SynchronizationContext.Current), () => Settings,
                (delay, ct) => { Delays.Add(delay); return Task.CompletedTask; });
        }
        private sealed class Protocol : IPlayServRoomProtocol
        {
            private PlayServRoomProtocolContext _context; private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>(); private bool _admitted;
            public bool Disposed;
            public PlayServRoomProtocolContext Context => _context;
            public TaskCompletionSource<bool> ExitWait;
            public Task EnterAsync(PlayServRoomProtocolContext context, CancellationToken ct)
            {
                _context = context;
                context.MessageReceived += text =>
                {
                    if (text.StartsWith("admitted:"))
                    {
                        if (text != "admitted:" + context.RoomName + ":" + context.PlayerId) _ready.TrySetException(new PlayServRoomEntryRefusedException("identity_mismatch"));
                        else _admitted = true;
                    }
                    if (text == "snapshot" && _admitted) _ready.TrySetResult(true);
                };
                ct.Register(() => _ready.TrySetCanceled());
                return _ready.Task;
            }
            public Task LeaveAsync(CancellationToken ct) => ExitWait?.Task ?? _context.SendTextAsync("leave", ct);
            public void Dispose() { Disposed = true; }
        }
        private sealed class Socket : ITransportImplementation, IObservable<byte[]>, IPlayServTransportCloseInfoSource
        {
            public readonly List<string> Sent = new List<string>(); public bool AutoReady, Disposed;
            public string Player;
            private IObserver<byte[]> _observer, _old;
            public event Action<PlayServTransportCloseInfo> Closed;
            public Task<bool> Connect() => Task.FromResult(true);
            public Task Send(byte[] data)
            {
                Sent.Add(Encoding.UTF8.GetString(data));
                if (AutoReady && Sent.Count == 1) { Receive("admitted:code:" + Player); Receive("snapshot"); }
                return Task.CompletedTask;
            }
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => this;
            public IDisposable Subscribe(IObserver<byte[]> observer) { _old = _observer = observer; return new Subscription(() => _observer = null); }
            public void Dispose() { Disposed = true; }
            public void Receive(string text) => _observer?.OnNext(Encoding.UTF8.GetBytes(text));
            public void ReceiveLate(string text) => _old?.OnNext(Encoding.UTF8.GetBytes(text));
            public void RemoteClose() => Closed?.Invoke(new PlayServTransportCloseInfo(1006, "lost"));
            private sealed class Subscription : IDisposable { private readonly Action _close; public Subscription(Action close) { _close = close; } public void Dispose() => _close(); }
        }
    }
}
