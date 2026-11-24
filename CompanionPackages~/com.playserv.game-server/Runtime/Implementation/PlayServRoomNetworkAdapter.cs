using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>One physical connection generation. Create a new object on reconnect; never reuse it.</summary>
    public interface IPlayServRoomNetworkConnection
    {
        /// <summary>Enable gameplay for this verified player. Invoked on the adapter's Unity thread.</summary>
        void Accept(string verifiedPlayerId);
        /// <summary>Disable gameplay and close this exact connection before returning. Must be idempotent.</summary>
        void Close(string reason);
    }

    /// <summary>Game-owned networking. Keep gameplay disabled until Accept; callbacks may originate on workers.</summary>
    public interface IPlayServRoomNetworkLifecycle
    {
        event Action<IPlayServRoomNetworkConnection, string> Authenticating;
        event Action<IPlayServRoomNetworkConnection> Disconnected;
    }

    /// <summary>Opt-in room lifetime owner for networking callbacks. Create and use public methods on Unity's main thread.</summary>
    public sealed class PlayServRoomNetworkAdapter : IDisposable
    {
        private readonly IPlayServRoomNetworkLifecycle _network;
        private readonly PlayServRoomConnectionTracker _tracker;
        private readonly SynchronizationContext _unity;
        private readonly int _thread;
        private readonly ConditionalWeakTable<IPlayServRoomNetworkConnection, Entry> _seen = new ConditionalWeakTable<IPlayServRoomNetworkConnection, Entry>();
        private readonly Dictionary<string, Entry> _active = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _players = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool _disposed;

        private sealed class Entry
        {
            internal readonly string Id = Guid.NewGuid().ToString("N");
            internal readonly IPlayServRoomNetworkConnection Connection;
            internal string Player, Admission;
            internal bool Closed;
            internal Entry(IPlayServRoomNetworkConnection connection) { Connection = connection; }
        }

        public PlayServRoomNetworkAdapter(PlayServGameRoomHandle room, IPlayServRoomNetworkLifecycle network, TimeSpan? reconnectGrace = null)
        {
            PlayServGameServer.EnsureSupportedBuildForServices();
            if (room == null) throw new ArgumentNullException(nameof(room));
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _unity = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the network adapter on Unity's main thread.");
            _thread = Thread.CurrentThread.ManagedThreadId;
            _tracker = room.CreateConnectionTracker(reconnectGrace);
            _tracker.ConnectionRemoved += OnRemoved;
            _tracker.Closed += OnTrackerClosed;
            try
            {
                _network.Authenticating += OnAuthenticating;
                _network.Disconnected += OnDisconnected;
            }
            catch { Dispose(); throw new InvalidOperationException("Could not subscribe to game networking callbacks."); }
        }

        public PlayServError LastError { get; private set; } = PlayServError.None;
        /// <summary>Safe diagnostic for failed game callbacks. A close failure is not a confirmed disconnection.</summary>
        public event Action<PlayServError> Error;

        public bool TryGetPlayerId(IPlayServRoomNetworkConnection connection, out string playerId)
        {
            CheckThread();
            playerId = null;
            return !_disposed && connection != null && _seen.TryGetValue(connection, out var entry) && !entry.Closed &&
                _tracker.TryGetPlayerId(entry.Id, out playerId);
        }

        /// <summary>Removes presence immediately and closes the live connection. Callback failures are reported through Error/LastError.</summary>
        public bool RemovePlayer(string playerId)
        {
            CheckThread();
            if (_disposed || playerId == null) return false;
            _players.TryGetValue(playerId, out var entry);
            var removed = _tracker.RemovePlayer(playerId);
            if (removed && entry != null) Close(entry, "player_removed");
            return removed;
        }

        private void OnAuthenticating(IPlayServRoomNetworkConnection connection, string token) =>
            Dispatch(() => Authenticate(connection, token));

        private void Authenticate(IPlayServRoomNetworkConnection connection, string token)
        {
            if (connection == null) { Report("network_connection_missing"); return; }
            if (_seen.TryGetValue(connection, out _)) return;
            var entry = new Entry(connection);
            _seen.Add(connection, entry);
            if (_disposed) { Close(entry, "room_closed"); return; }
            PlayServRoomAdmissionResult admission;
            try { admission = _tracker.TryAdmit(entry.Id, token); }
            catch (ArgumentException) { Close(entry, "reservation_invalid"); return; }
            catch { Close(entry, "room_closed"); return; }
            if (!admission.Ok) { Close(entry, admission.Error.SourceCode); return; }
            entry.Player = admission.PlayerId; entry.Admission = admission.AdmissionId;
            _active.Add(entry.Id, entry); _players[entry.Player] = entry;
            try { connection.Accept(entry.Player); }
            catch
            {
                // Accept may re-enter the source and replace this connection before throwing.
                _tracker.RemoveConnection(entry.Id);
                Close(entry, "network_accept_failed");
                Report("network_accept_failed");
            }
        }

        private void OnDisconnected(IPlayServRoomNetworkConnection connection) => Dispatch(() =>
        {
            if (connection == null || _disposed) return;
            if (!_seen.TryGetValue(connection, out var entry))
            {
                _seen.Add(connection, new Entry(connection) { Closed = true });
                return;
            }
            if (entry.Closed) return;
            Forget(entry);
            _tracker.ReportDisconnected(entry.Id);
        });

        private void OnRemoved(PlayServConnectionRemoval removal) => Dispatch(() =>
        {
            if (_active.TryGetValue(removal.ConnectionId, out var entry) && entry.Admission == removal.AdmissionId)
                Close(entry, removal.Reason);
        });

        private void Forget(Entry entry)
        {
            entry.Closed = true;
            _active.Remove(entry.Id);
            if (entry.Player != null && _players.TryGetValue(entry.Player, out var current) && ReferenceEquals(current, entry))
                _players.Remove(entry.Player);
        }

        private void Close(Entry entry, string reason)
        {
            if (entry.Closed) return;
            Forget(entry);
            try { entry.Connection.Close(reason); }
            catch { Report("network_close_failed"); }
        }

        private void OnTrackerClosed(bool operatorOwned) => Dispatch(() => Stop(!operatorOwned));

        /// <summary>Detaches callbacks, removes tracked presence and closes owned live connections. Keep this owner across scenes.</summary>
        public void Dispose() { CheckThread(); Stop(true); }

        private void Stop(bool closeConnections)
        {
            if (_disposed) return;
            _disposed = true;
            try { _network.Authenticating -= OnAuthenticating; } catch { Report("network_subscription_failed"); }
            try { _network.Disconnected -= OnDisconnected; } catch { Report("network_subscription_failed"); }
            _tracker.ConnectionRemoved -= OnRemoved;
            _tracker.Closed -= OnTrackerClosed;
            _tracker.Dispose();
            foreach (var entry in _active.Values.ToArray())
            {
                if (closeConnections) Close(entry, "tracker_closed");
                else Forget(entry); // Operator handlers retain ownership of actual game cleanup.
            }
        }

        private void Dispatch(Action callback)
        {
            if (Thread.CurrentThread.ManagedThreadId == _thread) callback();
            else _unity.Post(_ => callback(), null);
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("Use the network adapter on its Unity thread.");
        }
        private void Report(string code)
        {
            var error = new PlayServError(PlayServErrorCode.Transport, code, "The game networking callback failed: " + code + ".");
            LastError = error;
            if (Error == null) return;
            foreach (Action<PlayServError> observer in Error.GetInvocationList())
                try { observer(error); } catch { }
        }
    }
}
