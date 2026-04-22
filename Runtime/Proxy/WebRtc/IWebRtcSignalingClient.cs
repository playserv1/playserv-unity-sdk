using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Proxy.WebRtc
{
    /// <summary>
    /// Application-provided signaling bridge used by PlayServ WebRTC transport.
    /// </summary>
    public interface IWebRtcSignalingClient : IDisposable
    {
        /// <summary>
        /// Raised when remote signaling message arrives from signaling backend.
        /// </summary>
        event Action<WebRtcSignalMessage> MessageReceived;

        /// <summary>
        /// Raised when signaling layer reports an unrecoverable error.
        /// </summary>
        event Action<Exception> ErrorReceived;

        /// <summary>
        /// Opens signaling session/channel.
        /// </summary>
        Task<bool> Connect(CancellationToken ct = default);

        /// <summary>
        /// Sends local signaling message to remote peer/backend.
        /// </summary>
        Task SendAsync(WebRtcSignalMessage message, CancellationToken ct = default);

        /// <summary>
        /// Resets current signaling session state.
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Generic signaling envelope for WebRTC offer/answer/ICE exchange.
    /// </summary>
    [Serializable]
    public sealed class WebRtcSignalMessage
    {
        public string MessageType { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string SdpType { get; set; } = string.Empty;
        public string Sdp { get; set; } = string.Empty;
        public string Candidate { get; set; } = string.Empty;
        public string SdpMid { get; set; } = string.Empty;
        public int? SdpMLineIndex { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Well-known signaling message types used by PlayServ WebRTC transport.
    /// </summary>
    public static class WebRtcSignalMessageTypes
    {
        public const string Hello = "hello";
        public const string HelloAck = "hello-ack";
        public const string Offer = "offer";
        public const string Answer = "answer";
        public const string IceCandidate = "ice-candidate";
        public const string Ready = "ready";
        public const string Error = "error";
    }
}
