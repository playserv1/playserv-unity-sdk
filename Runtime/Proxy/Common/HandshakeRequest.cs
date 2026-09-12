using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Initial handshake request sent after transport connect.
    /// </summary>
    [Serializable]
    public sealed class HandshakeRequest
    {
        /// <summary>
        /// Wire compatibility credential. Filled from the public client token or runtime player JWT.
        /// </summary>
        public string GameAccessToken { get; set; }

        /// <summary>
        /// Optional public runtime client token (<c>pk_*</c>) used by runtime-auth.
        /// </summary>
        public string ClientToken { get; set; }

        /// <summary>
        /// Optional runtime player JWT authorization value. Expected format: <c>Bearer ...</c>.
        /// </summary>
        public string Authorization { get; set; }

        /// <summary>
        /// SDK version.
        /// </summary>
        public string SdkVersion { get; set; }

        /// <summary>
        /// Game client version.
        /// </summary>
        public string GameVersion { get; set; }

    }
}
