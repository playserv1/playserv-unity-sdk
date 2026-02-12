using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Initial handshake request sent after websocket connect.
    /// </summary>
    [Serializable]
    public sealed class HandshakeRequest
    {
        /// <summary>
        /// Game access token.
        /// </summary>
        public string GameAccessToken { get; set; }

        /// <summary>
        /// SDK version.
        /// </summary>
        public string SdkVersion { get; set; }

        /// <summary>
        /// Game client version.
        /// </summary>
        public string GameVersion { get; set; }

        /// <summary>
        /// Current user id.
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Game id.
        /// </summary>
        public string GameId { get; set; }
    }
}
