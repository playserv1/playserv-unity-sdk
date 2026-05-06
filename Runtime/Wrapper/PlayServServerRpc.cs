#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
using Playserv.RPC;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Server-side/in-process RPC surface for PlayServ SDK.
    /// </summary>
    public static class PlayServServerRpc
    {
        private static IPlayServServerRpcApi Api => PlayServApiHost.ServerRpc;

        public static void SetRpcInvoker(IRpcInvoker rpcInvoker) => Api.SetRpcInvoker(rpcInvoker);
    }
}

#endif
