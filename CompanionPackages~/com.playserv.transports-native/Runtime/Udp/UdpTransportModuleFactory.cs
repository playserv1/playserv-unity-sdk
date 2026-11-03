using System;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Implementation
{
    internal sealed class UdpTransportModuleFactory : ITransportModuleFactory
    {
        public string Scheme => "udp";

        public ITransportImplementation Create(TransportModuleContext context)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException(
                "UDP transport is not supported in Unity WebGL. Use ws://, wss:// or webrtc:// endpoint.");
#else
            return new UdpTransportImplementation(context.Endpoint, context.Logger);
#endif
        }
    }
}
