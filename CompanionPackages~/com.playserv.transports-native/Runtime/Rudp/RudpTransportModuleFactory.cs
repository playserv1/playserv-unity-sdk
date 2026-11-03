using System;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Implementation
{
    internal sealed class RudpTransportModuleFactory : ITransportModuleFactory
    {
        public string Scheme => "rudp";

        public ITransportImplementation Create(TransportModuleContext context)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException(
                "RUDP transport is not supported in Unity WebGL. Use ws://, wss:// or webrtc:// endpoint.");
#else
            return new RudpTransportImplementation(context.Endpoint, context.Logger);
#endif
        }
    }
}
