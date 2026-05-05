#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
namespace Playserv.RPC
{
    public interface IPlayServClientRpcModule
    {
        string InvokeModuleServiceName { get; }
    }
}

#endif
