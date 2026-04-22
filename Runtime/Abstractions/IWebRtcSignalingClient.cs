using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Application-provided signaling bridge used by PlayServ WebRTC transport.
    /// </summary>
    public interface IWebRtcSignalingClient : IDisposable
    {
        event Action<WebRtcSignalMessage> MessageReceived;
        event Action<Exception> ErrorReceived;

        Task<bool> Connect(CancellationToken ct = default);
        Task SendAsync(WebRtcSignalMessage message, CancellationToken ct = default);
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
