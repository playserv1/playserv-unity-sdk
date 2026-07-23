using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Server;
using Playserv.Wrapper;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.ServerId,
    typeof(Playserv.Server.PlayServServerModule),
    50)]
[assembly: PlayServLegacyApi(
    PlayServModuleManifest.ServerId,
    typeof(IPlayServServerRpcApi),
    typeof(Playserv.Wrapper.PlayServApiServerFacade))]
[assembly: PlayServLocalExecutionFactory(
    typeof(Playserv.Server.PlayServServerLocalExecution),
    100)]

namespace Playserv.Server
{
    internal static class PlayServServerModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServServerModule>(
                PlayServModuleManifest.ServerId,
                50);
            PlayServLegacyApiRegistry.Register<IPlayServServerRpcApi, PlayServApiServerFacade>(
                PlayServModuleManifest.ServerId,
                () => new PlayServApiServerFacade());
            PlayServLocalExecutionFactoryRegistry.Register(
                100,
                () => new PlayServServerLocalExecution());
        }
    }
}

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiServerFacade : IPlayServServerRpcApi
    {
        public void SetCommandHandler(ICommandHandler commandHandler) =>
            PlayServServerRpc.SetCommandHandler(commandHandler);

        public void SetEventHandler(IEventHandler eventHandler) =>
            PlayServServerRpc.SetEventHandler(eventHandler);

        public void SetRpcInvoker(IRpcInvoker rpcInvoker) =>
            PlayServServerRpc.SetRpcInvoker(rpcInvoker);
    }
}
