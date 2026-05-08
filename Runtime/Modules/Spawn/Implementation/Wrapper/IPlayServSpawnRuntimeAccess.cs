using Playserv.Modules;

namespace Playserv.Wrapper
{
    internal interface IPlayServSpawnRuntimeAccess
    {
        IPlayServModuleServiceProvider RequiredServices { get; }

        IPlayServModuleServiceProvider CurrentServices { get; }
    }
}
