using Playserv.Modules;

namespace Playserv.Wrapper
{
    internal interface IPlayServDataRuntimeAccess
    {
        IPlayServModuleServiceProvider RequiredServices { get; }
    }
}
