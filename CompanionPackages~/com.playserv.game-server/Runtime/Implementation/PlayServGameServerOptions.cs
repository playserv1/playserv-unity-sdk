using System;

namespace Playserv.GameServer
{
    /// <summary>Configuration for the process-wide Unity Dedicated Server client.</summary>
    public sealed class PlayServGameServerOptions
    {
        /// <summary>Runtime HTTP endpoint. Falls back to <c>PLAYSERV_API_URL</c>.</summary>
        public string BackendServerAddress { get; set; }

        /// <summary>Optional rotating key provider. Falls back to <c>PLAYSERV_SERVER_KEY</c>.</summary>
        public IPlayServServerKeyProvider ServerKeyProvider { get; set; }

        /// <summary>Room heartbeat interval in the inclusive range of one to ten seconds.</summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Default request timeout. Matchmaking long polls derive their own deadline.</summary>
        public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
