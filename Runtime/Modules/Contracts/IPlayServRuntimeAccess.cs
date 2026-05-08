namespace Playserv.Modules
{
    public interface IPlayServRuntimeAccess
    {
        bool HasCurrentInstance { get; }

        IPlayServCommandBus CurrentCommandBus { get; }

        IPlayServCommandBus RequiredCommandBus { get; }

        IPlayServModuleServiceProvider CurrentModuleServices { get; }

        IPlayServModuleServiceProvider RequiredModuleServices { get; }

        IPlayServCommandBus GetCommandBusForFireAndForget(string operationName);

        IPlayServModuleServiceProvider GetModuleServicesForFireAndForget(string operationName);
    }
}
