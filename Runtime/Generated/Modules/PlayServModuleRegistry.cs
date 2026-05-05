using System;
using Playserv.Modules;

namespace Playserv.Modules
{
    public static partial class PlayServModuleRegistry
    {
        public static void RegisterDefaults(PlayServModuleHost host)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            host.Register(new PlayServProvidedModule(PlayServModuleIds.Transport));
            host.Register(new PlayServProvidedModule(PlayServModuleIds.Serialization));
            RegisterGenerated(host);
        }

        static partial void RegisterGenerated(PlayServModuleHost host);
    }
}
