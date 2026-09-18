using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    public enum PlayServRoomSessionState { Idle, Entering, Ready, Reconnecting, Closed, Failed }

    public sealed class PlayServRoomSessionOptions
    {
        public TimeSpan EntryTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromSeconds(120);
        public int MaxReconnectAttempts { get; set; } = 3;
        public TimeSpan ExitTimeout { get; set; } = TimeSpan.FromSeconds(3);
        public PlayServGameConnectionOptions Connection { get; set; } = new PlayServGameConnectionOptions();
    }

    /// <summary>A new instance is required per connection. Enter must validate admission and initial game state before completing.</summary>
    public interface IPlayServRoomProtocol : IDisposable
    {
        Task EnterAsync(PlayServRoomProtocolContext context, CancellationToken ct);
        Task LeaveAsync(CancellationToken ct);
    }

    public sealed class PlayServRoomProtocolContext
    {
        private readonly Func<string,CancellationToken,Task> _send;
        private bool _closed;
        private readonly Action<PlayServError> _reject;
        internal PlayServRoomProtocolContext(string room, string player, Func<string,CancellationToken,Task> send, Action<PlayServError> reject)
        { RoomName = room; PlayerId = player; _send = send; _reject = reject; }
        public string RoomName { get; }
        public string PlayerId { get; }
        public event Action<string> MessageReceived;
        public Task SendTextAsync(string message, CancellationToken ct = default)
        {
            if (_closed) throw new ObjectDisposedException(nameof(PlayServRoomProtocolContext));
            return _send(message, ct);
        }
        public void Reject(PlayServError error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            if (!_closed) _reject(error);
        }
        internal void Receive(string text) { if (!_closed) MessageReceived?.Invoke(text); }
        internal void Close() { _closed = true; MessageReceived = null; }
    }
}
