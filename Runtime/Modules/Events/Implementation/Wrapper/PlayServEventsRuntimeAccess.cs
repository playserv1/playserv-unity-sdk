using System;
using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed class PlayServEventsRuntimeAccess : IPlayServEventsRuntimeAccess
    {
        public ILocalEventExecution LocalExecution =>
            PlayServRuntimeHost.LocalExecution as ILocalEventExecution ??
            throw new InvalidOperationException("Events local execution is not available.");

        public bool HasCurrentInstance => PlayServRuntimeHost.HasCurrentInstance;

        public IPlayServModuleServiceProvider RequiredServices => PlayServRuntimeHost.RequiredModuleServices;

        public IPlayServModuleServiceProvider GetServicesForFireAndForget(string operationName) =>
            PlayServRuntimeHost.GetModuleServicesForFireAndForget(operationName);
    }
}
