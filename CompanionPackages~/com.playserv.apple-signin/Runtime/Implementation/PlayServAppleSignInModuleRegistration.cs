using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.AppleSignInId,
    typeof(Playserv.AppleSignIn.PlayServAppleSignInModule),
    80)]

namespace Playserv.AppleSignIn
{
    internal static class PlayServAppleSignInModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServAppleSignInModule>(
                PlayServModuleManifest.AppleSignInId,
                80);
        }
    }
}
