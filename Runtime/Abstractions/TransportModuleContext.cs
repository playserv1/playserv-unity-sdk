using System;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.Runtime.Abstractions
{
    internal sealed class TransportModuleContext
    {
        public TransportModuleContext(
            string endpoint,
            ILogger logger,
            PlayServRuntimeSettings settings = null,
            Func<PlayServRuntimeSettings, IWebRtcSignalingClient> webRtcSignalingClientFactory = null,
            IJsonCodec jsonCodec = null)
        {
            Endpoint = endpoint ?? string.Empty;
            Logger = logger;
            Settings = settings?.Clone();
            WebRtcSignalingClientFactory = webRtcSignalingClientFactory;
            JsonCodec = jsonCodec;
        }

        public string Endpoint { get; }
        public ILogger Logger { get; }
        public PlayServRuntimeSettings Settings { get; }
        public Func<PlayServRuntimeSettings, IWebRtcSignalingClient> WebRtcSignalingClientFactory { get; }
        public IJsonCodec JsonCodec { get; }
    }
}
