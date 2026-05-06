#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_RPC_CORE && !PLAYSERV_MODULE_DISABLED_SERVER_RPC && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
using Playserv.RPC;

namespace Playserv.Wrapper
{
    public static partial class PlayServ
    {
        public static void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            PlayServServerRpc.SetRpcInvoker(rpcInvoker);
    }
}
#endif
