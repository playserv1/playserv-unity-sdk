using Playserv.Modules;

namespace Playserv.RPC
{
    public sealed class PlayServServerRpcModule : IPlayServModule, IPlayServServerRpcModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.ServerRpc,
            isCore: false,
            PlayServModuleIds.RpcCore,
            PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            context.Services.Register<IPlayServServerRpcModule>(this);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
