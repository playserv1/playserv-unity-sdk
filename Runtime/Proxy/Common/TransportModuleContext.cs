using System;
using Playserv.Proxy.Logging;
using Playserv.Proxy.WebRtc;
using Playserv.Wrapper;

namespace Playserv.Proxy.Common
{
    internal sealed class TransportModuleContext
    {
        public TransportModuleContext(
            string endpoint,
            ILogger logger,
            PlayServSettings settings = null,
            Func<PlayServSettings, IWebRtcSignalingClient> webRtcSignalingClientFactory = null)
        {
            Endpoint = endpoint ?? string.Empty;
            Logger = logger;
            Settings = settings;
            WebRtcSignalingClientFactory = webRtcSignalingClientFactory;
        }

        public string Endpoint { get; }
        public ILogger Logger { get; }
        public PlayServSettings Settings { get; }
        public Func<PlayServSettings, IWebRtcSignalingClient> WebRtcSignalingClientFactory { get; }
    }
}
