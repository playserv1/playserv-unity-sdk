using System;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

namespace Playserv.Proxy.WebRtc
{
    internal sealed class WebRtcSignalingProtocol
    {
        private readonly IJsonCodec _jsonCodec;
        private readonly string _sessionId;

        public WebRtcSignalingProtocol(IJsonCodec jsonCodec, string sessionId)
        {
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            _sessionId = sessionId ?? string.Empty;
        }

        public string Serialize(WebRtcSignalMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (string.IsNullOrWhiteSpace(message.SessionId))
                message.SessionId = _sessionId;

            return _jsonCodec.Serialize(message);
        }

        public bool TryDeserializeIncoming(string json, out WebRtcSignalMessage message, out Exception error)
        {
            message = null;
            error = null;

            try
            {
                var candidate = _jsonCodec.Deserialize<WebRtcSignalMessage>(json);
                if (candidate == null)
                    return false;

                if (!string.IsNullOrWhiteSpace(candidate.SessionId) &&
                    !string.Equals(candidate.SessionId, _sessionId, StringComparison.Ordinal))
                {
                    return false;
                }

                message = candidate;
                return true;
            }
            catch (Exception ex)
            {
                error = new InvalidOperationException(
                    $"Failed to parse WebRTC signaling message. Payload={json}. Error={ex.Message}",
                    ex);
                return false;
            }
        }
    }
}
