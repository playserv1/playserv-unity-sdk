#if !PLAYSERV_DISABLE_RPC
namespace Playserv.RPC
{
    public interface IPlayServRpcModule
    {
        string InvokeModuleServiceName { get; }
    }
}

#endif
