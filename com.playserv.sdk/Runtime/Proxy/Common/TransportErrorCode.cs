namespace Playserv.Proxy.Common
{
    public enum TransportErrorCode
    {
        None = 0,
        InvalidHandshakePayload = 00001,
        SdkVersionUnsupported = 01002,
        GameVersionMismatch = 01003,
        ConnectionLimitReached = 02001,
        SessionForceRejected = 02002
    }
}
