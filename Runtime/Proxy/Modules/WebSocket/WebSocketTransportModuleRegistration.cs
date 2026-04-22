using System.Collections.Generic;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    internal static partial class TransportModuleRegistry
    {
        static partial void RegisterWebSocket(List<ITransportModuleFactory> factories)
        {
            factories.Add(new Playserv.Proxy.Implementation.WebSocketTransportModuleFactory("http"));
            factories.Add(new Playserv.Proxy.Implementation.WebSocketTransportModuleFactory("https"));
            factories.Add(new Playserv.Proxy.Implementation.WebSocketTransportModuleFactory("ws"));
            factories.Add(new Playserv.Proxy.Implementation.WebSocketTransportModuleFactory("wss"));
        }
    }
}
