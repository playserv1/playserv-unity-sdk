namespace Playserv.Proxy.Common
{
    public sealed class HandshakeResult
    {
        public bool Success { get; }
        public TransportError Error { get; }

        private HandshakeResult(bool success, TransportError error = null)
        {
            Success = success;
            Error = error;
        }

        public static HandshakeResult Successful() => new(true);

        public static HandshakeResult Failed(TransportError error) => new(false, error);

        public static HandshakeResult Failed(TransportErrorCode code) => new(false, TransportError.FromCode(code));
    }
}
