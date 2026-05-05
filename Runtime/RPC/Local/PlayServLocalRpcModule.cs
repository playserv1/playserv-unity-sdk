#if !PLAYSERV_DISABLE_RPC && !PLAYSERV_DISABLE_LOCAL_RPC
using Playserv.Modules;

namespace Playserv.RPC
{
    public sealed class PlayServLocalRpcModule : IPlayServModule, IPlayServLocalRpcModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.LocalRpc,
            isCore: false,
            PlayServModuleIds.Rpc,
            PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            context.Services.Register<IPlayServLocalRpcModule>(this);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}

#endif
