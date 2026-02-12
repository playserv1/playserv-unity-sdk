namespace Playserv.Proxy.Common
{
    /// <summary>
    /// Result wrapper for handshake operation.
    /// </summary>
    public sealed class HandshakeResult
    {
        /// <summary>
        /// Indicates handshake success state.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Transport error when handshake failed.
        /// </summary>
        public TransportError Error { get; }

        private HandshakeResult(bool success, TransportError error = null)
        {
            Success = success;
            Error = error;
        }

        /// <summary>
        /// Creates successful handshake result.
        /// </summary>
        /// <returns>Successful result.</returns>
        public static HandshakeResult Successful() => new(true);

        /// <summary>
        /// Creates failed handshake result with explicit error.
        /// </summary>
        /// <param name="error">Transport error instance.</param>
        /// <returns>Failed result.</returns>
        public static HandshakeResult Failed(TransportError error) => new(false, error);

        /// <summary>
        /// Creates failed handshake result using predefined error message by code.
        /// </summary>
        /// <param name="code">Transport error code.</param>
        /// <returns>Failed result.</returns>
        public static HandshakeResult Failed(TransportErrorCode code) => new(false, TransportError.FromCode(code));
    }
}
