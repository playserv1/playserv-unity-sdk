using Playserv.Modules;
using Playserv.Wrapper;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.ClientRpcId,
    typeof(Playserv.RPC.PlayServClientRpcModule),
    40)]
[assembly: PlayServLegacyApi(
    PlayServModuleManifest.ClientRpcId,
    typeof(IPlayServRpcApi),
    typeof(Playserv.Wrapper.PlayServApiRpcFacade))]

namespace Playserv.RPC
{
    internal static class PlayServClientRpcModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServClientRpcModule>(
                PlayServModuleManifest.ClientRpcId,
                40);
            PlayServLegacyApiRegistry.Register<IPlayServRpcApi, PlayServApiRpcFacade>(
                PlayServModuleManifest.ClientRpcId,
                () => new PlayServApiRpcFacade());
        }
    }
}
