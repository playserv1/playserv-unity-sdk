using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.SteamAuthId,
    typeof(Playserv.SteamAuth.PlayServSteamAuthModule),
    93)]

namespace Playserv.SteamAuth
{
    internal static class PlayServSteamAuthModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServSteamAuthModule>(PlayServModuleManifest.SteamAuthId, 93);
        }
    }
}
