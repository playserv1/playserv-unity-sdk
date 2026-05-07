namespace Playserv.Server
{
    public interface ILocalCommandExecutionConfigurator
    {
        void SetCommandHandler(ICommandHandler commandHandler);
    }

    public interface ILocalEventExecutionConfigurator
    {
        void SetEventHandler(IEventHandler eventHandler);
    }

    public interface ILocalRpcExecutionConfigurator
    {
        void SetRpcInvoker(object rpcInvoker);
    }
}
