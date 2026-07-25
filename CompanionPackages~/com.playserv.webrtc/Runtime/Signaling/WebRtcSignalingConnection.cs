using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.WebRtc
{
    internal interface IWebRtcSignalingConnection : IDisposable
    {
        event Action Opened;
        event Action<string> MessageReceived;
        event Action<Exception> ErrorReceived;
        event Action<string> Closed;

        Task Connect(CancellationToken ct);
        Task SendAsync(string message, CancellationToken ct);
        void Reset();
    }

    internal static class WebRtcSignalingConnection
    {
        private const string WebSocketScheme = "ws";
        private const string SecureWebSocketScheme = "wss";
        private const string HttpScheme = "http";
        private const string HttpsScheme = "https";

        public static IWebRtcSignalingConnection Create(Uri uri, ILogger logger)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGlWebRtcSignalingBridgeAdapter(uri, logger);
#else
            return new NativeWebRtcSignalingConnection(uri, logger);
#endif
        }

        public static Uri BuildWebSocketUri(string address)
        {
            var value = (address ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("WebRTC signaling server address is empty.");

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"Invalid WebRTC signaling server address: {value}");

            if (string.Equals(uri.Scheme, HttpScheme, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri) { Scheme = WebSocketScheme, Port = uri.IsDefaultPort ? 80 : uri.Port };
                return builder.Uri;
            }

            if (string.Equals(uri.Scheme, HttpsScheme, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri) { Scheme = SecureWebSocketScheme, Port = uri.IsDefaultPort ? 443 : uri.Port };
                return builder.Uri;
            }

            if (string.Equals(uri.Scheme, WebSocketScheme, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, SecureWebSocketScheme, StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            throw new InvalidOperationException(
                $"Unsupported WebRTC signaling scheme '{uri.Scheme}'. Use ws://, wss://, http:// or https://.");
        }
    }
}
