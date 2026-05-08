using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal interface IPlayServEventsRuntimeAccess
    {
        ILocalEventExecution LocalExecution { get; }

        bool HasCurrentInstance { get; }

        IPlayServModuleServiceProvider RequiredServices { get; }

        IPlayServModuleServiceProvider GetServicesForFireAndForget(string operationName);
    }
}
