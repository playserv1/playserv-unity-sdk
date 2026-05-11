using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed class PlayServEventsRuntimeAccess : IPlayServEventsRuntimeAccess
    {
        private static readonly ILocalEventExecution NoOpLocalExecution = new NoOpPlayServLocalExecution();

        public ILocalEventExecution LocalExecution =>
            PlayServRuntimeHost.LocalExecution as ILocalEventExecution ?? NoOpLocalExecution;

        public bool HasCurrentInstance => PlayServRuntimeHost.HasCurrentInstance;

        public IPlayServModuleServiceProvider RequiredServices => PlayServRuntimeHost.RequiredModuleServices;

        public IPlayServModuleServiceProvider GetServicesForFireAndForget(string operationName) =>
            PlayServRuntimeHost.GetModuleServicesForFireAndForget(operationName);
    }
}
