using System;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.WebRtc;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebRtcTransportModuleFactory : ITransportModuleFactory
    {
        public string Scheme => "webrtc";

        public ITransportImplementation Create(TransportModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var settings = context.Settings?.Clone()
                ?? throw new InvalidOperationException(
                    "WebRTC transport requires PlayServSettings context. Use PlayServAPI transport resolver path.");

            IWebRtcSignalingClient signalingClient = null;
            if (context.WebRtcSignalingClientFactory != null)
                signalingClient = context.WebRtcSignalingClientFactory(settings);

            if (signalingClient == null && !string.IsNullOrWhiteSpace(settings.WebRtcSignalingServerAddress))
                signalingClient = new WebSocketWebRtcSignalingClient(settings, context.Logger);

            return new WebRtcDataChannelTransportImplementation(
                context.Endpoint,
                settings,
                signalingClient,
                context.Logger);
        }
    }
}
