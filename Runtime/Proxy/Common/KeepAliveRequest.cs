using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class KeepAliveRequest
    {
        public long Timestamp { get; set; }
    }
}
