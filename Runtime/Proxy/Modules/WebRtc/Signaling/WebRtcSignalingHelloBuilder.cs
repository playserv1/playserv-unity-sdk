using System;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

namespace Playserv.Proxy.WebRtc
{
    internal sealed class WebRtcSignalingHelloBuilder
    {
        private readonly PlayServRuntimeSettings _settings;
        private readonly IJsonCodec _jsonCodec;
        private readonly string _sessionId;

        public WebRtcSignalingHelloBuilder(PlayServRuntimeSettings settings, IJsonCodec jsonCodec, string sessionId)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            _sessionId = sessionId ?? string.Empty;
        }

        public string Build()
        {
            var hello = new WebRtcSignalingHelloMessage
            {
                MessageType = WebRtcSignalMessageTypes.Hello,
                SessionId = _sessionId,
                GameAccessToken = _settings.GameAccessToken ?? string.Empty,
                GameId = _settings.GameId ?? string.Empty,
                UserId = _settings.UserId ?? string.Empty,
                GameVersion = _settings.GameVersion ?? string.Empty,
                SdkVersion = _settings.SdkVersion ?? string.Empty,
                ChannelLabel = _settings.WebRtcDataChannelLabel ?? string.Empty,
                TransportEndpoint = _settings.BackendServerAddress ?? string.Empty
            };

            return _jsonCodec.Serialize(hello);
        }

        private sealed class WebRtcSignalingHelloMessage
        {
            public string MessageType { get; set; }
            public string SessionId { get; set; }
            public string GameAccessToken { get; set; }
            public string GameId { get; set; }
            public string UserId { get; set; }
            public string GameVersion { get; set; }
            public string SdkVersion { get; set; }
            public string ChannelLabel { get; set; }
            public string TransportEndpoint { get; set; }
        }
    }
}
