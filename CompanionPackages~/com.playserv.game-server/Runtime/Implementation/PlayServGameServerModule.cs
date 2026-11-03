using System;
using Playserv.Modules;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: PlayServModule(
    PlayServModuleManifest.GameServerId,
    typeof(Playserv.GameServer.PlayServGameServerModule),
    155)]

namespace Playserv.GameServer
{
    public sealed class PlayServGameServerModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.GameServer,
            isCore: false,
            PlayServModuleIds.Data,
            PlayServModuleIds.Analytics);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            context.Services.Register(this);
        }

        public void Shutdown()
        {
            PlayServGameServer.CancelForModuleShutdown();
        }
    }

    internal static class PlayServGameServerModuleRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PlayServModuleRegistry.Register<PlayServGameServerModule>(
                PlayServModuleManifest.GameServerId,
                155);
            Application.quitting -= PlayServGameServer.OnApplicationQuitting;
            Application.quitting += PlayServGameServer.OnApplicationQuitting;
        }
    }
}
