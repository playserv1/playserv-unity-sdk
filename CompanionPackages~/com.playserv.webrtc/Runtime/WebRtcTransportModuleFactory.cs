using System;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.WebRtc;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

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
                    "WebRTC transport requires runtime settings context.");
            var jsonCodec = context.JsonCodec
                ?? throw new InvalidOperationException("WebRTC transport requires JSON codec context.");

            IWebRtcSignalingClient signalingClient = null;
            if (context.WebRtcSignalingClientFactory != null)
                signalingClient = context.WebRtcSignalingClientFactory(settings);

            if (signalingClient == null && !string.IsNullOrWhiteSpace(settings.WebRtcSignalingServerAddress))
                signalingClient = new WebSocketWebRtcSignalingClient(settings, jsonCodec, context.Logger);

            return new WebRtcDataChannelTransportImplementation(
                context.Endpoint,
                settings,
                signalingClient,
                jsonCodec,
                context.Logger);
        }
    }
}
