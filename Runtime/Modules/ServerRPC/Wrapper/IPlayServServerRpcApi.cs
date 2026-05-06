using Playserv.RPC;

namespace Playserv.Wrapper
{
    public interface IPlayServServerRpcApi
    {
        void SetRpcInvoker(IRpcInvoker rpcInvoker);
    }
}
