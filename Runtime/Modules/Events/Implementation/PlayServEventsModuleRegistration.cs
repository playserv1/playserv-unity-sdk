using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.EventsId,
    typeof(Playserv.Events.PlayServEventsModule),
    10)]

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
        }
    }
}
