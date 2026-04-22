using System;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebSocketTransportModuleFactory : ITransportModuleFactory
    {
        public WebSocketTransportModuleFactory(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme))
                throw new ArgumentException("Scheme is required.", nameof(scheme));

            Scheme = scheme;
        }

        public string Scheme { get; }

        public ITransportImplementation Create(TransportModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLWebSocketTransportImplementation(context.Endpoint, context.Logger);
#else
            return new WebSocketTransportImplementation(context.Endpoint, context.Logger);
#endif
        }
    }
}
