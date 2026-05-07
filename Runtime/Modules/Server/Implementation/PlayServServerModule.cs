using Playserv.Modules;

namespace Playserv.RPC
{
    public sealed class PlayServServerModule : IPlayServModule, IPlayServServerModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Server,
            isCore: false,
            PlayServModuleIds.RpcCore,
            PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            context.Services.Register<IPlayServServerModule>(this);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
