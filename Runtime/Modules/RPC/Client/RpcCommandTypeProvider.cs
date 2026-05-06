using Playserv.Proxy.Common;

namespace Playserv.RPC
{
    internal sealed class RpcCommandTypeProvider : ICommandTypeProvider
    {
        public void RegisterCommandTypes(CommandTypeRegistryBuilder builder)
        {
            builder.Register<InvokeRpc>();
            builder.Register<RpcInvokeRequest>("InvokeRpcRequest");
            builder.Register<InvokeRpcResponse>();
        }
    }
}
