using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Keepalive pong payload.
    /// </summary>
    [Serializable]
    public sealed class KeepAliveResponse
    {
        private long timestamp;

        /// <summary>
        /// UTC unix timestamp in milliseconds.
        /// </summary>
        public long Timestamp
        {
            get => timestamp;
            set => timestamp = value;
        }
    }
}
