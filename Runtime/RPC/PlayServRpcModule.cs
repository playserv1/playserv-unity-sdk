#if !PLAYSERV_DISABLE_RPC
using System;
using Playserv.Modules;

namespace Playserv.RPC
{
    public sealed class PlayServRpcModule : IPlayServModule, IPlayServRpcModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Rpc,
            isCore: false,
            PlayServModuleIds.Transport,
            PlayServModuleIds.Serialization);

        public string InvokeModuleServiceName => RpcConstants.InvokeModuleServiceName;

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Services.Register<IPlayServRpcModule>(this);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}

#endif
