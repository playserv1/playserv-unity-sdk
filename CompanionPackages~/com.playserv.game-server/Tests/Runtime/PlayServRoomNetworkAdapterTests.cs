using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Wrapper;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServRoomNetworkAdapterTests
    {
        private const string Player = "plr_01J8ZKQ4X0";
        private H.AdmissionTestScope _scope;
        private H.Socket _socket;
        private H.Http _http;
        private PlayServGameRoomHandle _room;
        private PlayServRoomNetworkAdapter _adapter;
        private Network _network;
        private double _now;
        private Func<bool> _operatorRemove, _operatorClose;

        [SetUp] public void Setup()
        {
            var delivery = SynchronizationContext.Current;
            Assert.That(delivery, Is.Not.Null, "This fixture must run with Unity's delivery context.");
            _scope = new H.AdmissionTestScope(); _network = new Network(); _now = 100;
            PlayServGameServer.CancelForModuleShutdown();
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(),
                ParticipantRemoveHandler = (_, __) => Task.FromResult(_operatorRemove?.Invoke() ?? false),
                RoomCloseHandler = (_, __) => Task.FromResult(_operatorClose?.Invoke() ?? false)
            },
                _http = new H.Http(), () => DateTimeOffset.FromUnixTimeSeconds(1000), (_, ct) => Task.Delay(Timeout.Infinite, ct));
            var context = PlayServGameServer.GetContextForServices();
            context.Dispatch = action => delivery.Post(_ => action(), null);
            context.MonotonicSeconds = () => _now;
            PlayServGameServer.Uplink.Seconds = () => _now;
            _scope.Track(PlayServGameServer.Uplink);
            PlayServGameServer.Uplink.SocketFactory = () => _socket = new H.Socket(H.Ack());
        }

        [UnityTearDown] public IEnumerator Cleanup() => H.Run(async () =>
        {
            _adapter?.Dispose();
            await _scope.CleanupAsync();
        });

        private async Task Start(bool attach = true)
        {
            _room = await PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest("arena", new PlayServGameRoomSnapshot("one", 0, 8)));
            _scope.Track(_room);
            if (attach) _adapter = new PlayServRoomNetworkAdapter(_room, _network);
        }
        private async Task Offer(string token = "rsv_test", string player = Player)
        {
            _socket.Push(PlayServPushedAdmissionTests.Offer(token, player: player));
            await H.Until(() => _socket.Sent.Any(s => s.Contains("\"ticket_result\"") && s.Contains(token) && s.Contains("\"ok\":true")));
        }
        private async Task Ack(string token = "rsv_test", bool ok = true)
        {
            _socket.Push("{\"type\":\"join_ack\",\"reservation_token\":\"" + token + "\",\"room_name\":\"one\",\"player_id\":\"" + Player + "\",\"ok\":" + (ok ? "true" : "false") + ",\"reason\":\"reservation_expired\"}");
            var pongs = _socket.Sent.Count(s => s == "{\"type\":\"pong\"}");
            _socket.Push("{\"type\":\"ping\"}");
            await H.Until(() => _socket.Sent.Count(s => s == "{\"type\":\"pong\"}") > pongs);
        }
        private int Frames(string kind) => _socket.Sent.Count(s => s.Contains("\"event\":\"" + kind + "\""));
        private Connection Connect(string token = "rsv_test")
        {
            var connection = new Connection();
            connection.OnClose = () => _network.Drop(connection);
            _network.Authenticate(connection, token);
            return connection;
        }

        [UnityTest] public IEnumerator InvalidTicketClosesOnlyItsConnectionWithoutPresence() => H.Run(async () =>
        {
            await Start();
            var connection = Connect("rsv_unknown");
            Assert.That(connection.Closes, Is.EqualTo(1));
            Assert.That(connection.Accepts, Is.Zero);
            Assert.That(connection.Reason, Is.EqualTo("reservation_invalid"));
            Assert.That(_room.RosterSnapshot(), Is.Empty);
            await _room.DrainPresenceForTesting(); Assert.That(Frames("join"), Is.Zero);
        });

        [UnityTest] public IEnumerator ValidTicketBindsVerifiedPlayerBeforeGameplayAndDuplicateCallbackDoesNotJoinAgain() => H.Run(async () =>
        {
            await Start(); await Offer();
            var connection = new Connection();
            connection.OnAccept = () => Assert.That(_adapter.TryGetPlayerId(connection, out var id) && id == Player, Is.True);
            _network.Authenticate(connection, "rsv_test");
            _network.Authenticate(connection, "rsv_test");
            Assert.That(connection.Accepts, Is.EqualTo(1));
            Assert.That(connection.PlayerId, Is.EqualTo(Player));
            await _room.DrainPresenceForTesting(); Assert.That(Frames("join"), Is.EqualTo(1));
            Assert.That(_room.RosterSnapshot(), Is.EqualTo(new[] { Player }));
        });

        [UnityTest] public IEnumerator ReconnectReplacesParkedConnectionAndIgnoresOldDisconnectAndOldRejection() => H.Run(async () =>
        {
            await Start(); await Offer(); var old = Connect();
            await _room.DrainPresenceForTesting();
            _network.Drop(old); await Offer("rsv_new"); var current = Connect("rsv_new");
            await _room.DrainPresenceForTesting(); await Ack(ok: false);
            _network.Drop(old);
            Assert.That(_adapter.TryGetPlayerId(current, out var id) && id == Player, Is.True);
            Assert.That(current.Closes, Is.Zero);
            Assert.That(_room.RosterSnapshot(), Has.Length.EqualTo(1));
            Assert.That(Frames("join"), Is.EqualTo(2));
            Assert.That(Frames("leave"), Is.Zero);
        });

        [UnityTest] public IEnumerator LivePlayerCannotBeReplacedAndConsumedTicketCannotReconnect() => H.Run(async () =>
        {
            await Start(); await Offer(); var first = Connect(); await _room.DrainPresenceForTesting(); await Ack();
            await Offer("rsv_second"); var intruder = Connect("rsv_second");
            Assert.That(intruder.Closes, Is.EqualTo(1)); Assert.That(first.Closes, Is.Zero);
            _network.Drop(first); var replay = Connect();
            Assert.That(replay.Accepts, Is.Zero); Assert.That(replay.Reason, Is.EqualTo("reservation_consumed"));
            Assert.That(_room.RosterSnapshot(), Has.Length.EqualTo(1));
        });

        [UnityTest] public IEnumerator DuplicateDisconnectDoesNotExtendGrace() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = Connect(); await _room.DrainPresenceForTesting(); await Ack();
            _network.Drop(connection); _now += 29;
            await _room.EvaluateLifetimeAsync();
            Assert.That(_room.RosterSnapshot(), Has.Length.EqualTo(1));
            _network.Drop(connection); _now++;
            await _room.EvaluateLifetimeAsync();
            await _room.DrainPresenceForTesting();
            Assert.That(_room.RosterSnapshot(), Is.Empty); Assert.That(Frames("leave"), Is.EqualTo(1));
            Assert.That(_adapter.TryGetPlayerId(connection, out _), Is.False);
        });

        [UnityTest] public IEnumerator ExplicitRemovalClosesOnceAndItsDisconnectDoesNotParkThePlayer() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = Connect(); await _room.DrainPresenceForTesting(); await Ack();
            Assert.That(_adapter.RemovePlayer(Player), Is.True);
            Assert.That(connection.Closes, Is.EqualTo(1));
            Assert.That(_adapter.RemovePlayer(Player), Is.False);
            await _room.DrainPresenceForTesting();
            Assert.That(Frames("leave"), Is.EqualTo(1)); Assert.That(_room.RosterSnapshot(), Is.Empty);
        });

        [UnityTest] public IEnumerator LatePlatformRejectionClosesTheAdmittedConnection() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = Connect();
            await _room.DrainPresenceForTesting(); await Ack(ok: false);
            await H.Until(() => connection.Closes == 1);
            Assert.That(connection.Reason, Is.EqualTo("reservation_expired"));
            Assert.That(_adapter.TryGetPlayerId(connection, out _), Is.False);
        });

        [UnityTest] public IEnumerator NetworkCallbacksFromWorkerThreadAreDeliveredOnUnityThread() => H.Run(async () =>
        {
            await Start(); await Offer(); var thread = Thread.CurrentThread.ManagedThreadId;
            var connection = new Connection();
            await Task.Run(() => _network.Authenticate(connection, "rsv_test"));
            await H.Until(() => connection.Accepts == 1);
            Assert.That(connection.CallbackThread, Is.EqualTo(thread));
        });

        [UnityTest] public IEnumerator DisconnectedConnectionCannotBeAdmittedByALateAuthenticationCallback() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = new Connection();
            _network.Drop(connection); _network.Authenticate(connection, "rsv_test");
            Assert.That(connection.Accepts, Is.Zero); Assert.That(_room.RosterSnapshot(), Is.Empty);
        });

        [UnityTest] public IEnumerator AcceptFailureRemovesPresenceAndHidesTransportException() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = new Connection { OnAccept = () => throw new Exception("rsv_private_secret") };
            _network.Authenticate(connection, "rsv_test");
            Assert.That(connection.Closes, Is.EqualTo(1)); Assert.That(_room.RosterSnapshot(), Is.Empty);
            Assert.That(_adapter.LastError.SourceCode, Is.EqualTo("network_accept_failed"));
            Assert.That(_adapter.LastError.ToString(), Does.Not.Contain("rsv_private_secret"));
        });

        [UnityTest] public IEnumerator CloseFailureDoesNotPreventOtherConnectionsFromBeingClosed() => H.Run(async () =>
        {
            await Start(); await Offer(); var first = Connect();
            first.OnClose = () => throw new Exception("private-close-secret");
            await Offer("rsv_other", "plr_01J8ZKQ4X1"); var second = Connect("rsv_other");
            _adapter.Dispose();
            Assert.That(first.Closes, Is.EqualTo(1)); Assert.That(second.Closes, Is.EqualTo(1));
            Assert.That(_adapter.LastError.SourceCode, Is.EqualTo("network_close_failed"));
            Assert.That(_adapter.LastError.Message, Does.Not.Contain("private-close-secret"));
            Assert.That(_network.Subscribers, Is.Zero);
        });

        [UnityTest] public IEnumerator FailedAcceptAfterDisconnectDoesNotLeaveAParkedPlayer() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = new Connection();
            connection.OnAccept = () => { _network.Drop(connection); throw new Exception("accept failed"); };
            _network.Authenticate(connection, "rsv_test");
            Assert.That(_room.RosterSnapshot(), Is.Empty);
            await _room.DrainPresenceForTesting(); Assert.That(Frames("leave"), Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator FailedOldAcceptCannotRemoveAReentrantReplacement() => H.Run(async () =>
        {
            await Start(); await Offer(); await Offer("rsv_new");
            var old = new Connection(); Connection current = null;
            old.OnAccept = () => { _network.Drop(old); current = Connect("rsv_new"); throw new Exception("old accept failed"); };
            _network.Authenticate(old, "rsv_test");
            Assert.That(_adapter.TryGetPlayerId(current, out var id) && id == Player, Is.True);
            Assert.That(current.Closes, Is.Zero); Assert.That(_room.RosterSnapshot(), Has.Length.EqualTo(1));
            await _room.DrainPresenceForTesting(); Assert.That(Frames("leave"), Is.Zero);
        });

        [UnityTest] public IEnumerator ClosingRoomRejectsAdapterBeforeItCanSubscribe() => H.Run(async () =>
        {
            await Start(attach: false);
            var response = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, ct) => response.Task;
            var closing = _room.CloseAsync();
            try
            {
                Assert.Throws<InvalidOperationException>(() => _adapter = new PlayServRoomNetworkAdapter(_room, _network));
                Assert.That(_network.Subscribers, Is.Zero);
            }
            finally { response.TrySetResult(new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{}" }); await closing; }
        });

        [UnityTest] public IEnumerator ModuleShutdownDetachesAndClosesOwnedConnections() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = Connect();
            PlayServGameServer.CancelForModuleShutdown();
            await H.Until(() => _network.Subscribers == 0);
            Assert.That(connection.Closes, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator RoomCloseDetachesEvenAnEmptyAdapter() => H.Run(async () =>
        {
            await Start(); Assert.That(_network.Subscribers, Is.EqualTo(2));
            await _room.CloseAsync(); await H.Until(() => _network.Subscribers == 0);
            _adapter.Dispose();
        });

        [UnityTest] public IEnumerator DisposeClosesConnectionsAndRejectsAlreadyQueuedAuthentication() => H.Run(async () =>
        {
            await Start(); await Offer(); var first = Connect();
            var queued = _network.AuthenticationCallback; var late = new Connection();
            Assert.That(queued, Is.Not.Null);
            _adapter.Dispose(); _adapter.Dispose(); queued(late, "rsv_test");
            Assert.That(first.Closes, Is.EqualTo(1)); Assert.That(late.Closes, Is.EqualTo(1));
            Assert.That(late.Accepts, Is.Zero); Assert.That(_network.Subscribers, Is.Zero);
        });

        [UnityTest] public IEnumerator SceneChangeKeepsBindingWithoutAdditionalPresence() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = Connect(); await _room.DrainPresenceForTesting(); await Ack();
            var previous = SceneManager.GetActiveScene();
            var next = SceneManager.CreateScene("NetworkAdapterScene" + Guid.NewGuid().ToString("N"));
            try
            {
                SceneManager.SetActiveScene(next);
                _network.Authenticate(connection, "rsv_test");
                Assert.That(_adapter.TryGetPlayerId(connection, out var id) && id == Player, Is.True);
                Assert.That(connection.Accepts, Is.EqualTo(1));
                await _room.DrainPresenceForTesting(); Assert.That(Frames("join"), Is.EqualTo(1)); Assert.That(Frames("leave"), Is.Zero);
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                var unload = SceneManager.UnloadSceneAsync(next);
                while (!unload.isDone) await Task.Yield();
            }
        });

        [UnityTest] public IEnumerator UnsubscribeFailureStillClosesConnectionsAndRemovesPresence() => H.Run(async () =>
        {
            await Start(); await Offer(); var connection = Connect();
            _network.ThrowOnUnsubscribe = true;
            Assert.DoesNotThrow(() => _adapter.Dispose());
            Assert.That(connection.Closes, Is.EqualTo(1));
            Assert.That(_room.RosterSnapshot(), Is.Empty);
            Assert.That(_network.Subscribers, Is.Zero);
            Assert.That(_adapter.LastError.SourceCode, Is.EqualTo("network_subscription_failed"));
            Assert.That(_adapter.LastError.Message, Does.Not.Contain("rsv_private_secret"));
        });

        [UnityTest] public IEnumerator OperatorRemovalKeepsActualCloseOwnedByTheGameHandler() => H.Run(async () =>
        {
            var unityThread = Thread.CurrentThread.ManagedThreadId;
            await Start(); await Offer(); var connection = Connect(); await _room.DrainPresenceForTesting(); await Ack();
            _operatorRemove = () => { connection.Close("operator_remove"); return true; };
            _socket.Push("{\"type\":\"participant_remove\",\"request_id\":\"remove\",\"room_name\":\"one\",\"player_id\":\"" + Player + "\"}");
            await H.Until(() => _socket.Sent.Any(s => s.Contains("participant_remove_result")));
            Assert.That(_socket.Sent.Single(s => s.Contains("participant_remove_result")), Does.Contain("\"ok\":true"));
            Assert.That(connection.CallbackThread, Is.EqualTo(unityThread), "Operator cleanup must run on Unity's thread.");
            Assert.That(connection.Closes, Is.EqualTo(1));
            Assert.That(_adapter.TryGetPlayerId(connection, out _), Is.False);
            Assert.That(_room.RosterSnapshot(), Is.Empty); Assert.That(Frames("leave"), Is.EqualTo(1));
            _adapter.Dispose(); Assert.That(connection.Closes, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator OperatorCloseDetachesWithoutDuplicatingTheGameHandlersCleanup() => H.Run(async () =>
        {
            var unityThread = Thread.CurrentThread.ManagedThreadId;
            await Start(); await Offer(); var connection = Connect(); await _room.DrainPresenceForTesting(); await Ack();
            _operatorClose = () => { connection.Close("operator_close"); return true; };
            _socket.Push("{\"type\":\"room_close\",\"request_id\":\"close\",\"room_name\":\"one\"}");
            await H.Until(() => _socket.Sent.Any(s => s.Contains("room_close_result")));
            Assert.That(_socket.Sent.Single(s => s.Contains("room_close_result")), Does.Contain("\"ok\":true"));
            Assert.That(connection.CallbackThread, Is.EqualTo(unityThread), "Operator cleanup must run on Unity's thread.");
            await H.Until(() => _network.Subscribers == 0);
            _adapter.Dispose(); Assert.That(connection.Closes, Is.EqualTo(1));
            Assert.That(_adapter.TryGetPlayerId(connection, out _), Is.False);
        });

        [UnityTest] public IEnumerator PlayerBuildAndExistingTrackerRejectAdapterBeforeSubscribing() => H.Run(async () =>
        {
            PlayServGameServer.SetSupportedBuildForTesting(false);
            try { Assert.Throws<PlatformNotSupportedException>(() => new PlayServRoomNetworkAdapter(null, _network)); }
            finally { PlayServGameServer.SetSupportedBuildForTesting(null); }
            await Start();
            var other = new Network();
            Assert.Throws<InvalidOperationException>(() => new PlayServRoomNetworkAdapter(_room, other));
            Assert.That(other.Subscribers, Is.Zero);
        });

        private sealed class Network : IPlayServRoomNetworkLifecycle
        {
            private Action<IPlayServRoomNetworkConnection, string> _authenticating;
            private Action<IPlayServRoomNetworkConnection> _disconnected;
            internal bool ThrowOnUnsubscribe;
            public event Action<IPlayServRoomNetworkConnection, string> Authenticating
            {
                add => _authenticating += value;
                remove { _authenticating -= value; if (ThrowOnUnsubscribe) throw new Exception("rsv_private_secret"); }
            }
            public event Action<IPlayServRoomNetworkConnection> Disconnected
            {
                add => _disconnected += value;
                remove { _disconnected -= value; if (ThrowOnUnsubscribe) throw new Exception("rsv_private_secret"); }
            }
            internal Action<IPlayServRoomNetworkConnection, string> AuthenticationCallback => _authenticating;
            internal int Subscribers => (_authenticating?.GetInvocationList().Length ?? 0) + (_disconnected?.GetInvocationList().Length ?? 0);
            internal void Authenticate(Connection connection, string ticket) => _authenticating?.Invoke(connection, ticket);
            internal void Drop(Connection connection) => _disconnected?.Invoke(connection);
        }
        private sealed class Connection : IPlayServRoomNetworkConnection
        {
            internal int Accepts, Closes, CallbackThread;
            internal string PlayerId, Reason;
            internal Action OnAccept, OnClose;
            public void Accept(string verifiedPlayerId)
            { Accepts++; PlayerId = verifiedPlayerId; CallbackThread = Thread.CurrentThread.ManagedThreadId; OnAccept?.Invoke(); }
            public void Close(string reason)
            { Closes++; Reason = reason; CallbackThread = Thread.CurrentThread.ManagedThreadId; OnClose?.Invoke(); }
        }
    }
}
