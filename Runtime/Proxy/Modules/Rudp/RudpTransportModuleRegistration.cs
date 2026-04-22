using System.Collections.Generic;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    internal static partial class TransportModuleRegistry
    {
        static partial void RegisterRudp(List<ITransportModuleFactory> factories)
        {
            factories.Add(new Playserv.Proxy.Implementation.RudpTransportModuleFactory());
        }
    }
}
