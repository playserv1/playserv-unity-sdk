using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    internal static partial class TransportModuleRegistry
    {
        internal static ITransportModuleFactory[] GetFactories()
        {
            var factories = new List<ITransportModuleFactory>();
            RegisterWebSocket(factories);
            RegisterUdp(factories);
            RegisterRudp(factories);
            RegisterWebRtc(factories);

            return factories
                .OrderBy(x => x.Scheme, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static partial void RegisterWebSocket(List<ITransportModuleFactory> factories);
        static partial void RegisterUdp(List<ITransportModuleFactory> factories);
        static partial void RegisterRudp(List<ITransportModuleFactory> factories);
        static partial void RegisterWebRtc(List<ITransportModuleFactory> factories);
    }
}
