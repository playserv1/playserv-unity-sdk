using System;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task<string> BuildAsync(CancellationToken cancellationToken = default)
        {
            var runtimeAuthorization = _settings.RuntimeTokenProvider == null
                ? null
                : PlayServCredentialPolicy.NormalizePlayerAuthorization(
                    await _settings.RuntimeTokenProvider.GetTokenAsync(cancellationToken));
            var hello = new WebRtcSignalingHelloMessage
            {
                MessageType = WebRtcSignalMessageTypes.Hello,
                SessionId = _sessionId,
                GameAccessToken = ResolveWireCredential(_settings.ClientToken, runtimeAuthorization),
                GameVersion = _settings.GameVersion ?? string.Empty,
                SdkVersion = _settings.SdkVersion ?? string.Empty,
                ChannelLabel = _settings.WebRtcDataChannelLabel ?? string.Empty,
                TransportEndpoint = _settings.BackendServerAddress ?? string.Empty
            };

            return _jsonCodec.Serialize(hello);
        }

        private static string ResolveWireCredential(string clientToken, string authorization)
        {
            if (!string.IsNullOrWhiteSpace(authorization))
                return PlayServCredentialPolicy.ExtractBearerToken(authorization);

            return string.IsNullOrWhiteSpace(clientToken)
                ? string.Empty
                : PlayServCredentialPolicy.NormalizeClientToken(clientToken);
        }

        private sealed class WebRtcSignalingHelloMessage
        {
            public string MessageType { get; set; }
            public string SessionId { get; set; }
            public string GameAccessToken { get; set; }
            public string GameVersion { get; set; }
            public string SdkVersion { get; set; }
            public string ChannelLabel { get; set; }
            public string TransportEndpoint { get; set; }
        }
    }
}
