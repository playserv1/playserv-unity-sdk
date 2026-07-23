using Playserv.RPC;
using Playserv.Server;

namespace Playserv.Wrapper
{
    public interface IPlayServServerRpcApi
    {
        void SetCommandHandler(ICommandHandler commandHandler);

        void SetEventHandler(IEventHandler eventHandler);

        void SetRpcInvoker(IRpcInvoker rpcInvoker);
    }
}
