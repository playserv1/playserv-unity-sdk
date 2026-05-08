using System;
using Playserv.Modules;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    public interface IPlayServRuntimeSessionFactory
    {
        IPlayServRuntimeSession Create(
            string endpoint,
            PlayServTransportImplementationFactory transportImplementationFactory,
            Action<PlayServModuleHost> registerModules);
    }
}
