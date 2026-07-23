using Playserv.Modules;
using Playserv.Wrapper;
using UnityEngine;

[assembly: PlayServModule(
    PlayServModuleManifest.EventsId,
    typeof(Playserv.Events.PlayServEventsModule),
    10)]
[assembly: PlayServLegacyApi(
    PlayServModuleManifest.EventsId,
    typeof(IPlayServEventsApi),
    typeof(Playserv.Wrapper.PlayServApiEventsFacade))]

namespace Playserv.Events
{
    internal static class PlayServEventsModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServEventsModule>(
                PlayServModuleManifest.EventsId,
                10);
            PlayServLegacyApiRegistry.Register<IPlayServEventsApi, PlayServApiEventsFacade>(
                PlayServModuleManifest.EventsId,
                () => new PlayServApiEventsFacade());
        }
    }
}
