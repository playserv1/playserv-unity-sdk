using Playserv.Modules;
using Playserv.Wrapper;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.DataSubscriptionId,
    typeof(Playserv.DataSubscription.PlayServDataSubscriptionModule),
    20)]
[assembly: PlayServLegacyApi(
    PlayServModuleManifest.DataSubscriptionId,
    typeof(IPlayServDataApi),
    typeof(Playserv.Wrapper.PlayServApiDataFacade))]

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
            PlayServLegacyApiRegistry.Register<IPlayServDataApi, PlayServApiDataFacade>(
                PlayServModuleManifest.DataSubscriptionId,
                () => new PlayServApiDataFacade());
        }
    }
}
