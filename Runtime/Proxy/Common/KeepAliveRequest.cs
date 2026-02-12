using System;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Keepalive ping payload.
    /// </summary>
    [Serializable]
    public sealed class KeepAliveRequest
    {
        /// <summary>
        /// UTC unix timestamp in milliseconds.
        /// </summary>
        public long Timestamp { get; set; }
    }
}
