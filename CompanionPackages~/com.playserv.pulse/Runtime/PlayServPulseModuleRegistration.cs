using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.PulseId,
    typeof(Playserv.Pulse.PlayServPulseModule),
    70)]

namespace Playserv.Pulse
{
    internal static class PlayServPulseModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServPulseModule>(
                PlayServModuleManifest.PulseId,
                70);
        }
    }
}
