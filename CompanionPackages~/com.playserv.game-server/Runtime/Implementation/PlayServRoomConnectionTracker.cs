using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>A game-owned transport must disconnect this exact connection generation; the SDK never closes a game socket.</summary>
    public sealed class PlayServConnectionRemoval
    {
        internal PlayServConnectionRemoval(string connection, string player, string admission, string reason)
        { ConnectionId = connection; PlayerId = player; AdmissionId = admission; Reason = reason; }
        public string ConnectionId { get; }
        public string PlayerId { get; }
        public string AdmissionId { get; }
        public string Reason { get; }
    }

    /// <summary>Optional connection-to-player binding with monotonic reconnect grace. Do not mix with manual presence for tracked players.</summary>
    public sealed class PlayServRoomConnectionTracker : IDisposable
    {
        private readonly PlayServGameRoomHandle _room;
        private readonly PlayServGameServerContext _context;
        private readonly Dictionary<string, Entry> _players = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _connections = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool _disposed;
        private sealed class Entry
        { internal string Connection, Player, Admission; internal double? Until; }

        internal PlayServRoomConnectionTracker(PlayServGameRoomHandle room, PlayServGameServerContext context, TimeSpan grace)
        {
            if (grace < TimeSpan.Zero || grace.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(grace));
            _room = room; _context = context; ReconnectGrace = grace;
            room.Admission.AdmissionRejected += OnRejected;
            room.Terminated += OnTerminated;
        }
        public TimeSpan ReconnectGrace { get; }
        public event Action<PlayServConnectionRemoval> ConnectionRemoved;

        /// <summary>Consumes a fresh ticket; same-player replacement is allowed only during reconnect grace. Never log token.</summary>
        public PlayServRoomAdmissionResult TryAdmit(string connectionId, string token)
        {
            ValidateConnection(connectionId);
            Evaluate();
            lock (_room.AdmissionLock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PlayServRoomConnectionTracker));
                if (_connections.ContainsKey(connectionId)) return Failure("connection_already_tracked");
                var result = PlayServGameServer.Uplink.AdmitTicket(_room, token, player =>
                    _players.TryGetValue(player, out var old) && old.Until.HasValue ? old.Admission : null);
                if (!result.Ok) return result;
                if (_players.TryGetValue(result.PlayerId, out var previous)) _connections.Remove(previous.Connection);
                var entry = new Entry { Connection = connectionId, Player = result.PlayerId, Admission = result.AdmissionId };
                _players[entry.Player] = entry; _connections[connectionId] = entry;
                return result;
            }
        }
        public bool TryGetPlayerId(string connectionId, out string playerId)
        {
            lock (_room.AdmissionLock)
            {
                playerId = null;
                if (connectionId == null || !_connections.TryGetValue(connectionId, out var entry) || entry.Until.HasValue) return false;
                playerId = entry.Player; return true;
            }
        }
        /// <summary>A transient socket drop, not a quit. Duplicate notifications never extend the deadline.</summary>
        public void ReportDisconnected(string connectionId)
        {
            lock (_room.AdmissionLock)
            {
                if (_disposed || connectionId == null || !_connections.TryGetValue(connectionId, out var entry) || entry.Until.HasValue) return;
                entry.Until = _context.MonotonicSeconds() + ReconnectGrace.TotalSeconds;
            }
            Evaluate();
        }
        /// <summary>Immediate game-owned quit/kick, without reconnect grace.</summary>
        public bool RemovePlayer(string playerId)
        {
            Entry removed;
            lock (_room.AdmissionLock)
            {
                if (_disposed || playerId == null || !_players.TryGetValue(playerId, out removed)) return false;
                Remove(removed, true);
            }
            Notify(removed, "player_removed"); return true;
        }
        internal void Evaluate()
        {
            Entry[] expired;
            lock (_room.AdmissionLock)
            {
                if (_disposed) return;
                var now = _context.MonotonicSeconds();
                expired = _players.Values.Where(p => p.Until.HasValue && p.Until <= now).ToArray();
                foreach (var entry in expired) Remove(entry, true);
            }
            foreach (var entry in expired) Notify(entry, "reconnect_grace_expired");
        }
        private void Remove(Entry entry, bool leave)
        {
            _players.Remove(entry.Player); _connections.Remove(entry.Connection);
            if (leave) _room.RemoveAdmittedPlayer(entry.Player, entry.Admission);
        }
        private void OnRejected(PlayServRoomAdmissionResult rejection)
        {
            Entry entry;
            lock (_room.AdmissionLock)
            {
                if (_disposed || rejection.PlayerId == null || !_players.TryGetValue(rejection.PlayerId, out entry) || entry.Admission != rejection.AdmissionId) return;
                Remove(entry, false);
            }
            Notify(entry, rejection.Error.SourceCode);
        }
        private void Notify(Entry entry, string reason)
        {
            var notification = new PlayServConnectionRemoval(entry.Connection, entry.Player, entry.Admission, reason);
            _context.Post(() => ConnectionRemoved?.Invoke(notification));
        }
        private void OnTerminated(PlayServGameRoomHandle room, PlayServError error) => Dispose();
        public void Dispose()
        {
            Entry[] entries;
            lock (_room.AdmissionLock)
            {
                if (_disposed) return;
                _disposed = true; entries = _players.Values.ToArray();
                foreach (var entry in entries) Remove(entry, _room.CanAdmit);
                _room.Admission.AdmissionRejected -= OnRejected;
                _room.Terminated -= OnTerminated;
            }
            foreach (var entry in entries) Notify(entry, "tracker_closed");
        }
        private static PlayServRoomAdmissionResult Failure(string reason) =>
            new PlayServRoomAdmissionResult(false, null, null, new PlayServError(PlayServErrorCode.Transport, reason, "The game connection could not be admitted."));
        private static void ValidateConnection(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl)) throw new ArgumentException("Use a non-blank, generation-unique connection ID of at most 128 characters.", nameof(id));
        }
    }
}
