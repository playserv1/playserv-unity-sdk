using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.EpicAuthId,
    typeof(Playserv.EpicAuth.PlayServEpicAuthModule),
    92)]

namespace Playserv.EpicAuth
{
    internal static class PlayServEpicAuthModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServEpicAuthModule>(PlayServModuleManifest.EpicAuthId, 92);
        }
    }
}
