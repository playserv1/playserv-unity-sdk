using System;

namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Optional capability implemented by transports that can expose a remote close frame.
    /// Custom transports are not required to implement it.
    /// </summary>
    public interface IPlayServTransportCloseInfoSource
    {
        event Action<PlayServTransportCloseInfo> Closed;
    }

    public sealed class PlayServTransportCloseInfo
    {
        public PlayServTransportCloseInfo(int? statusCode, string reason)
        {
            StatusCode = statusCode;
            Reason = reason ?? string.Empty;
        }

        public int? StatusCode { get; }

        public string Reason { get; }
    }
}
