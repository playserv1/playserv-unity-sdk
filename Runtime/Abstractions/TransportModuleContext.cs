using System;
using Playserv.Proxy.Logging;

namespace Playserv.Runtime.Abstractions
{
    internal sealed class TransportModuleContext
    {
        public TransportModuleContext(
            string endpoint,
            ILogger logger,
            PlayServRuntimeSettings settings = null,
            Func<PlayServRuntimeSettings, IWebRtcSignalingClient> webRtcSignalingClientFactory = null)
        {
            Endpoint = endpoint ?? string.Empty;
            Logger = logger;
            Settings = settings?.Clone();
            WebRtcSignalingClientFactory = webRtcSignalingClientFactory;
        }

        public string Endpoint { get; }
        public ILogger Logger { get; }
        public PlayServRuntimeSettings Settings { get; }
        public Func<PlayServRuntimeSettings, IWebRtcSignalingClient> WebRtcSignalingClientFactory { get; }
    }
}
