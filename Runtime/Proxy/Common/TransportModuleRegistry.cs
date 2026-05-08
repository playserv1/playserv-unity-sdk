using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    internal static class TransportModuleRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ITransportModuleFactory> Factories =
            new Dictionary<string, ITransportModuleFactory>(StringComparer.OrdinalIgnoreCase);

        internal static void Register(ITransportModuleFactory factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (string.IsNullOrWhiteSpace(factory.Scheme))
                throw new ArgumentException("Transport module factory scheme cannot be empty.", nameof(factory));

            lock (Gate)
            {
                Factories[factory.Scheme] = factory;
            }

            TransportImplementationResolver.ClearFactoryCache();
        }

        internal static ITransportModuleFactory[] GetFactories()
        {
            lock (Gate)
            {
                return Factories.Values
                    .OrderBy(x => x.Scheme, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }
}
