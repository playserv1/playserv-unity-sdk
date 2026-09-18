using System;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    /// <summary>Lifecycle of one explicit game-server connection; none of these states confirms admission.</summary>
    public enum PlayServGameConnectionState
    {
        Created,
        Connecting,
        HandshakeSent,
        Closed
    }

    /// <summary>Options copied when creating a direct connection. No implicit reconnect or admission protocol.</summary>
    public sealed class PlayServGameConnectionOptions
    {
        /// <summary>Total credentials/open/handshake budget, and the budget for each subsequent send, including queue wait.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(12);
        /// <summary>Optional game-selected name carried in the C# server handshake, not a profile rename.</summary>
        public string DisplayName { get; set; }
        /// <summary>Explicit development opt-in to plaintext ws://. Default false; never disable TLS verification.</summary>
        public bool AllowInsecureWebSocket { get; set; }
    }

    /// <summary>Safe direct-connection failure, without peer payloads, tokens or underlying exception messages.</summary>
    public sealed class PlayServGameConnectionException : Exception
    {
        internal PlayServGameConnectionException(PlayServError error) : base(error.Message) => UnifiedError = error;
        public PlayServError UnifiedError { get; }
    }
}
