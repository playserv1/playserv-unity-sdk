using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed class PlayServDataRuntimeAccess : IPlayServDataRuntimeAccess
    {
        public IPlayServModuleServiceProvider RequiredServices => PlayServRuntimeHost.RequiredModuleServices;
    }
}
