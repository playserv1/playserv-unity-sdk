using System;
using Playserv.Modules;

namespace Playserv.RPC
{
    public sealed class PlayServRpcCoreModule : IPlayServModule, IPlayServRpcCoreModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.RpcCore,
            isCore: false,
            PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Services.Register<IPlayServRpcCoreModule>(this);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
