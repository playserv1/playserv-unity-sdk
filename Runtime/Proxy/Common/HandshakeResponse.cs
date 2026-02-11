using System;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class HandshakeResponse
    {
        public bool success;
        public int errorCode;
        public string errorMessage;
        public TransportErrorCode ErrorCode => (TransportErrorCode)errorCode;
    }
}
