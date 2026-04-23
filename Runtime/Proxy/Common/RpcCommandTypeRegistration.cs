using Playserv.RPC;

namespace Playserv.Proxy.Common
{
    internal static class RpcCommandTypeRegistration
    {
        public static void Register(CommandTypeRegistryBuilder builder)
        {
            builder.Register<InvokeRpc>();
            builder.Register<RpcInvokeRequest>("InvokeRpcRequest");
            builder.Register<InvokeRpcResponse>();
        }
    }
}
