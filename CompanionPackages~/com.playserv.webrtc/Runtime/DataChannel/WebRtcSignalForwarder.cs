using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebRtcSignalForwarder
    {
        private readonly IWebRtcSignalingClient _signalingClient;
        private readonly IJsonCodec _jsonCodec;
        private readonly IWebRtcPeerBridgeAdapter _peerBridge;
        private readonly ILogger _logger;
        private readonly SynchronizationContext _syncContext;
        private readonly string _endpoint;

        public WebRtcSignalForwarder(
            IWebRtcSignalingClient signalingClient,
            IJsonCodec jsonCodec,
            IWebRtcPeerBridgeAdapter peerBridge,
            ILogger logger,
            SynchronizationContext syncContext,
            string endpoint)
        {
            _signalingClient = signalingClient;
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            _peerBridge = peerBridge ?? throw new ArgumentNullException(nameof(peerBridge));
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
            _syncContext = syncContext ?? new SynchronizationContext();
            _endpoint = endpoint ?? string.Empty;
        }

        public void ForwardRemoteSignal(WebRtcSignalMessage message)
        {
            if (message == null)
                return;

            _syncContext.Post(_ =>
            {
                try
                {
                    var json = _jsonCodec.Serialize(message);
                    if (!_peerBridge.ApplySignal(json))
                    {
                        _logger.LogWarning(
                            $"WebRTC JS bridge did not accept remote signaling message. type={message.MessageType}, endpoint={_endpoint}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to forward remote signaling message to WebRTC JS bridge: {ex.Message}");
                }
            }, null);
        }

        public async Task ForwardLocalSignalAsync(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                var message = _jsonCodec.Deserialize<WebRtcSignalMessage>(json);
                if (message == null)
                {
                    _logger.LogError($"Failed to deserialize local WebRTC signaling message. payload={json}");
                    return;
                }

                await _signalingClient.SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to forward local WebRTC signaling message: {ex.Message}");
            }
        }
    }
}
