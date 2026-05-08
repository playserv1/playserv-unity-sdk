using System;
using Playserv.Modules;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    public sealed class PlayServRuntimeSessionFactory : IPlayServRuntimeSessionFactory
    {
        public IPlayServRuntimeSession Create(
            string endpoint,
            PlayServTransportImplementationFactory transportImplementationFactory,
            Action<PlayServModuleHost> registerModules)
        {
            return transportImplementationFactory == null
                ? new PlayServImplementation(endpoint, registerModules)
                : new PlayServImplementation(endpoint, transportImplementationFactory, registerModules);
        }
    }
}
