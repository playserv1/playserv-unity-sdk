using System;
using System.Collections.Generic;

namespace Playserv.Proxy.Common
{
    public static class CommandRouteProviderRegistry
    {
        private static readonly object Sync = new object();
        private static readonly List<ICommandRouteProvider> Providers = new List<ICommandRouteProvider>();
        private static readonly HashSet<Type> ProviderTypes = new HashSet<Type>();

        static CommandRouteProviderRegistry()
        {
            Register(new ProxyCommandRouteProvider());
        }

        public static void Register(ICommandRouteProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (Sync)
            {
                if (!ProviderTypes.Add(provider.GetType()))
                    return;

                Providers.Add(provider);
            }
        }

        internal static ICommandRouteProvider[] Snapshot()
        {
            lock (Sync)
                return Providers.ToArray();
        }
    }
}
