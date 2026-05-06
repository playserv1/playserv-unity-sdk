#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
using Playserv.RPC;

namespace Playserv.Wrapper
{
    public interface IPlayServServerRpcApi
    {
        void SetRpcInvoker(IRpcInvoker rpcInvoker);
    }
}

#endif
