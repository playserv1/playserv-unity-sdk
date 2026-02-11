using System;

namespace Playserv.Proxy.Common
{
    public sealed class TransportError
    {
        public TransportErrorCode Code { get; }
        public string Message { get; }

        public TransportError(TransportErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public static TransportError FromCode(TransportErrorCode code) => code switch
        {
            TransportErrorCode.InvalidHandshakePayload => new TransportError(code, "Invalid handshake payload. Check that GameAccessToken, SDKVersion, and GameVersion are provided."),
            TransportErrorCode.SdkVersionUnsupported => new TransportError(code, "SDK version is not supported. Please update your SDK."),
            TransportErrorCode.GameVersionMismatch => new TransportError(code, "Game version mismatch. Please update your game client."),
            TransportErrorCode.ConnectionLimitReached => new TransportError(code, "Connection limit reached. Please try again later."),
            TransportErrorCode.SessionForceRejected => new TransportError(code, "Connection rejected. Server is in maintenance mode."),
            _ => new TransportError(code, "Unknown transport error occurred.")
        };

        public override string ToString() => $"[{(int)Code:D5}] {Message}";
    }
}
