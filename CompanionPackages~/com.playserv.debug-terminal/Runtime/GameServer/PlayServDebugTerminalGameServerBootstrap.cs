using Playserv.DebugTerminal;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Playserv.DebugTerminal.GameServer
{
    internal static class PlayServDebugTerminalGameServerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            DebugTerminalCommandExtensionRegistry.Register(
                terminal => new PlayServDebugTerminalGameServerExtension(terminal));
        }
    }
}
