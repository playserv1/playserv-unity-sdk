using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.RpcCoreId,
    typeof(Playserv.RPC.PlayServRpcCoreModule),
    30)]

namespace Playserv.RPC
{
    internal static class PlayServRpcCoreModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServRpcCoreModule>(
                PlayServModuleManifest.RpcCoreId,
                30);
        }
    }
}
