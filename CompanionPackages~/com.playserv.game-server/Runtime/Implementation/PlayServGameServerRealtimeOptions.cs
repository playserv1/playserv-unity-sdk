using System;

namespace Playserv.GameServer
{
    /// <summary>Handshake metadata and keepalive settings for the opt-in server WebSocket session.</summary>
    public sealed class PlayServGameServerRealtimeOptions
    {
        /// <summary>Handshake game ID. Defaults to <c>Application.identifier</c>.</summary>
        public string GameId { get; set; }

        /// <summary>Process instance ID. Defaults to a process-stable random identifier.</summary>
        public string InstanceId { get; set; }

        /// <summary>Handshake game version. Defaults to <c>Application.version</c>.</summary>
        public string GameVersion { get; set; }

        /// <summary>Keepalive ping interval. Defaults to 30 seconds.</summary>
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Keepalive pong timeout. Defaults to 10 seconds.</summary>
        public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
