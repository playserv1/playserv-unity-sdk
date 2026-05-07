using Playserv.RPC;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Server-side/in-process RPC surface for PlayServ SDK.
    /// </summary>
    public static class PlayServServerRpc
    {
        public static void SetRpcInvoker(IRpcInvoker rpcInvoker)
        {
            var localExecution = PlayServRuntimeHost.LocalExecution as Playserv.Server.ILocalRpcExecutionConfigurator;
            if (localExecution == null)
                throw new System.InvalidOperationException("Server module is not installed or enabled.");

            localExecution.SetRpcInvoker(rpcInvoker);
        }
    }
}
