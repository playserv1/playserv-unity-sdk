namespace Playserv.Wrapper
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
        SessionForceRejected = 02002,

        /// <summary>
        /// Session was forcefully disconnected by server.
        /// </summary>
        ForcedDisconnect = 02003,

        /// <summary>
        /// The client ingress requires a signed-in player credential.
        /// </summary>
        PlayerCredentialRequired = 02004,

        /// <summary>
        /// The requested group belongs to another project.
        /// </summary>
        GroupOutsideProject = 02005,

        /// <summary>The server's group subscription count or group-key length limit was exceeded.</summary>
        GroupSubscriptionLimitReached = 02006
    }
}
