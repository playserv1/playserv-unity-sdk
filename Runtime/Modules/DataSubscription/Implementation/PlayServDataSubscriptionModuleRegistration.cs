using Playserv.Modules;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.DataSubscriptionId,
    typeof(Playserv.DataSubscription.PlayServDataSubscriptionModule),
    20)]

namespace Playserv.DataSubscription
{
    internal static class PlayServDataSubscriptionModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServDataSubscriptionModule>(
                PlayServModuleManifest.DataSubscriptionId,
                20);
        }
    }
}
