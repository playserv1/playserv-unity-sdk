using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.DebugTerminalId,
    typeof(Playserv.DebugTerminal.PlayServDebugTerminalModule),
    140)]

namespace Playserv.DebugTerminal
{
    public sealed class PlayServDebugTerminalModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } =
            new PlayServModuleDescriptor(
                PlayServModuleIds.DebugTerminal,
                isCore: false);

        public void Initialize(PlayServModuleContext context)
        {
        }

        public void Shutdown()
        {
        }
    }

    internal static class PlayServDebugTerminalModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServDebugTerminalModule>(
                PlayServModuleManifest.DebugTerminalId,
                140);
        }
    }
}
