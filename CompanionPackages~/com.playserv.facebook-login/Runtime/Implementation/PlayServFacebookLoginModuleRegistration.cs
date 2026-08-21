using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.FacebookLoginId,
    typeof(Playserv.FacebookLogin.PlayServFacebookLoginModule),
    91)]

namespace Playserv.FacebookLogin
{
    internal static class PlayServFacebookLoginModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServFacebookLoginModule>(PlayServModuleManifest.FacebookLoginId, 91);
        }
    }
}
