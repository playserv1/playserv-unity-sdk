using Playserv.RPC;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Server-side/in-process RPC surface for PlayServ SDK.
    /// </summary>
    public static class PlayServServerRpc
    {
        private static readonly IPlayServServerRuntimeAccess RuntimeAccess = new PlayServServerRuntimeAccess();

        public static void SetCommandHandler(Playserv.Server.ICommandHandler commandHandler)
        {
            var localExecution = RuntimeAccess.LocalExecution as Playserv.Server.ILocalCommandExecutionConfigurator;
            if (localExecution == null)
                throw new System.InvalidOperationException("Server module is not installed or enabled.");

            localExecution.SetCommandHandler(commandHandler);
        }

        public static void SetEventHandler(Playserv.Server.IEventHandler eventHandler)
        {
            var localExecution = RuntimeAccess.LocalExecution as Playserv.Server.ILocalEventExecutionConfigurator;
            if (localExecution == null)
                throw new System.InvalidOperationException("Server module is not installed or enabled.");

            localExecution.SetEventHandler(eventHandler);
        }

        public static void SetRpcInvoker(IRpcInvoker rpcInvoker)
        {
            var localExecution = RuntimeAccess.LocalExecution as Playserv.Server.ILocalRpcExecutionConfigurator;
            if (localExecution == null)
                throw new System.InvalidOperationException("Server module is not installed or enabled.");

            localExecution.SetRpcInvoker(rpcInvoker);
        }
    }
}
