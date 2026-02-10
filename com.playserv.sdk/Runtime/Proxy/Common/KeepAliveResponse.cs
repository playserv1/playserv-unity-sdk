using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class KeepAliveResponse
    {
        private long timestamp;

        public long Timestamp
        {
            get => timestamp;
            set => timestamp = value;
        }
    }
}
