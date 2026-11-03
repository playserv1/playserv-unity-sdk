using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Matchmaking;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples.Rooms
{
    /// <summary>Example game-owned protocol; this is not a platform-wide admission wire contract.</summary>
    public sealed class AdmissionSnapshotProtocol : IPlayServRoomProtocol
    {
        private readonly TaskCompletionSource<bool> _admitted = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<bool> _snapshot = new TaskCompletionSource<bool>();
        private PlayServRoomProtocolContext _context;
        private bool _enterSent;
        public string InitialSnapshotJson { get; private set; }
        public async Task EnterAsync(PlayServRoomProtocolContext context, CancellationToken ct)
        {
            _context = context;
            context.MessageReceived += Receive;
            using (ct.Register(() => { _admitted.TrySetCanceled(); _snapshot.TrySetCanceled(); }))
            {
                await _admitted.Task;
                _enterSent = true;
                await context.SendTextAsync("{\"method\":\"enter\"}", ct);
                await _snapshot.Task;
            }
        }
        private void Receive(string text)
        {
            var frame = JsonUtility.FromJson<Frame>(text);
            if (frame.type != "admitted" && frame.type != "snapshot" && frame.type != "refused") return;
            if (frame.type == "refused" || frame.roomName != _context.RoomName || frame.playerId != _context.PlayerId)
            {
                var error = new PlayServError(PlayServErrorCode.Forbidden, "game_admission_refused", "Game admission was refused or identified a different room/player.");
                _context.Reject(error);
                return;
            }
            if (frame.type == "admitted") _admitted.TrySetResult(true);
            else if (frame.type == "snapshot" && _enterSent)
            {
                InitialSnapshotJson = text;
                _snapshot.TrySetResult(true);
            }
        }
        public Task LeaveAsync(CancellationToken ct) => _context.SendTextAsync("{\"method\":\"leave\"}", ct);
        public void Dispose()
        {
            if (_context != null) _context.MessageReceived -= Receive;
            _admitted.TrySetCanceled(); _snapshot.TrySetCanceled();
        }
        [Serializable]
        private sealed class Frame { public string type; public string roomName; public string playerId; }
    }
}
