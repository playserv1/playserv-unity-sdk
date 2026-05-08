using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed class PlayServSpawnRuntimeAccess : IPlayServSpawnRuntimeAccess
    {
        public IPlayServModuleServiceProvider RequiredServices => PlayServRuntimeHost.RequiredModuleServices;

        public IPlayServModuleServiceProvider CurrentServices => PlayServRuntimeHost.CurrentModuleServices;
    }
}
