using System;
using Playserv.Wrapper;

namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Handshake response received from backend.
    /// </summary>
    [Serializable]
    public sealed class HandshakeResponse
    {
        /// <summary>
        /// True when handshake succeeded.
        /// </summary>
        public bool success;

        /// <summary>
        /// Numeric error code when handshake failed.
        /// </summary>
        public int errorCode;

        /// <summary>
        /// Error message returned by backend.
        /// </summary>
        public string errorMessage;

        /// <summary>
        /// Strongly typed transport error code.
        /// </summary>
        public TransportErrorCode ErrorCode => (TransportErrorCode)errorCode;
    }
}
