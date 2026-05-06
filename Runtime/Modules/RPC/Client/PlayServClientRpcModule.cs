#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
using System;
using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.RPC
{
    public sealed class PlayServClientRpcModule : IPlayServModule, IPlayServClientRpcModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.ClientRpc,
            isCore: false,
            PlayServModuleIds.RpcCore,
            PlayServModuleIds.Transport,
            PlayServModuleIds.Serialization);

        public string InvokeModuleServiceName => RpcConstants.InvokeModuleServiceName;

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            CommandTypeProviderRegistry.Register(new RpcCommandTypeProvider());
            CommandRouteProviderRegistry.Register(new RpcCommandRouteProvider());

            context.Services.Register<IPlayServClientRpcModule>(this);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}

#endif
