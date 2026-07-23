using Playserv.Modules;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.GoogleSignInId,
    typeof(Playserv.GoogleSignIn.PlayServGoogleSignInModule),
    90)]

namespace Playserv.GoogleSignIn
{
    internal static class PlayServGoogleSignInModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServGoogleSignInModule>(
                PlayServModuleManifest.GoogleSignInId,
                90);
        }
    }
}
