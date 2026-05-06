using System;
using System.Collections.Generic;

namespace Playserv.Proxy.Common
{
    public static class CommandTypeProviderRegistry
    {
        private static readonly object Sync = new object();
        private static readonly List<ICommandTypeProvider> Providers = new List<ICommandTypeProvider>();
        private static readonly HashSet<Type> ProviderTypes = new HashSet<Type>();

        static CommandTypeProviderRegistry()
        {
            Register(new ProxyCommandTypeProvider());
        }

        public static void Register(ICommandTypeProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (Sync)
            {
                if (!ProviderTypes.Add(provider.GetType()))
                    return;

                Providers.Add(provider);
                CommandTypeRegistry.Invalidate();
            }
        }

        internal static ICommandTypeProvider[] Snapshot()
        {
            lock (Sync)
                return Providers.ToArray();
        }
    }
}
