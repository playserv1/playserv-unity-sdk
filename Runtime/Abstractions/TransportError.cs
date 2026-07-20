using System;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Transport-level error model with code and message.
    /// </summary>
    public sealed class TransportError
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public TransportErrorCode Code { get; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Creates transport error.
        /// </summary>
        /// <param name="code">Error code.</param>
        /// <param name="message">Error message.</param>
        public TransportError(TransportErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        /// <summary>
        /// Creates transport error with predefined message for given code.
        /// </summary>
        /// <param name="code">Error code.</param>
        /// <returns>Transport error instance.</returns>
        public static TransportError FromCode(TransportErrorCode code) => code switch
        {
            TransportErrorCode.InvalidHandshakePayload => new TransportError(code, "Invalid handshake payload. Check that ClientToken or Authorization, SDKVersion, and GameVersion are provided."),
            TransportErrorCode.SdkVersionUnsupported => new TransportError(code, "SDK version is not supported. Please update your SDK."),
            TransportErrorCode.GameVersionMismatch => new TransportError(code, "Game version mismatch. Please update your game client."),
            TransportErrorCode.ConnectionLimitReached => new TransportError(code, "Connection limit reached. Please try again later."),
            TransportErrorCode.SessionForceRejected => new TransportError(code, "Connection rejected. Server is in maintenance mode."),
            TransportErrorCode.ForcedDisconnect => new TransportError(code, "Connection was terminated by the server."),
            _ => new TransportError(code, "Unknown transport error occurred.")
        };

        /// <summary>
        /// Returns formatted code + message string.
        /// </summary>
        /// <returns>Formatted error string.</returns>
        public override string ToString() => $"[{(int)Code:D5}] {Message}";
    }
}
