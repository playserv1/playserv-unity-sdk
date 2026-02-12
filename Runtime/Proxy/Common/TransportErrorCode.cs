namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Transport/handshake error codes returned by backend.
    /// </summary>
    public enum TransportErrorCode
    {
        /// <summary>
        /// No specific code.
        /// </summary>
        None = 0,

        /// <summary>
        /// Handshake payload is invalid.
        /// </summary>
        InvalidHandshakePayload = 00001,

        /// <summary>
        /// SDK version is not supported.
        /// </summary>
        SdkVersionUnsupported = 01002,

        /// <summary>
        /// Game version does not match server requirements.
        /// </summary>
        GameVersionMismatch = 01003,

        /// <summary>
        /// Too many concurrent connections.
        /// </summary>
        ConnectionLimitReached = 02001,

        /// <summary>
        /// Session rejected by server policy.
        /// </summary>
        SessionForceRejected = 02002
    }
}
