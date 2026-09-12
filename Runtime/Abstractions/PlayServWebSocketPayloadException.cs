using System;

namespace Playserv.Wrapper
{
    public enum PlayServWebSocketPayloadDirection
    {
        Outbound = 0,
        Inbound = 1
    }

    /// <summary>Raised before a WebSocket payload can exceed the backend's 1 MiB bound.</summary>
    public sealed class PlayServWebSocketPayloadException : InvalidOperationException
    {
        internal PlayServWebSocketPayloadException(
            PlayServWebSocketPayloadDirection direction,
            long actualBytes)
            : base($"WebSocket {direction.ToString().ToLowerInvariant()} payload exceeds the maximum size of {PlayServWebSocketPayloadLimits.MaxMessageBytes} bytes.")
        {
            Direction = direction;
            ActualBytes = actualBytes;
            MaxBytes = PlayServWebSocketPayloadLimits.MaxMessageBytes;
            UnifiedError = PlayServError.FromTransport(
                1009,
                "websocket_payload_too_large",
                Message,
                retryable: false);
        }

        public PlayServWebSocketPayloadDirection Direction { get; }
        public long ActualBytes { get; }
        public int MaxBytes { get; }
        public PlayServError UnifiedError { get; }
    }

    /// <summary>Payload limits shared by desktop and WebGL WebSocket transports.</summary>
    public static class PlayServWebSocketPayloadLimits
    {
        public const int MaxMessageBytes = 1024 * 1024;

        internal static void EnsureAllowed(
            long actualBytes,
            PlayServWebSocketPayloadDirection direction)
        {
            if (actualBytes > MaxMessageBytes)
                throw new PlayServWebSocketPayloadException(direction, actualBytes);
        }
    }
}
