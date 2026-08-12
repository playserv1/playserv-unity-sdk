using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.ClientRpcId,
    typeof(Playserv.RPC.PlayServClientRpcModule),
    40)]

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
        }
    }
}
