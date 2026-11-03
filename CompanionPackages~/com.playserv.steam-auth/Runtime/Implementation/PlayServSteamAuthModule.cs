using System;
using Playserv.Modules;
using Playserv.Wrapper;

namespace Playserv.SteamAuth
{
    public sealed class PlayServSteamAuthModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.SteamAuth,
            isCore: false,
            PlayServModuleIds.ClientExecution);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            context.Services.Register<IPlayServSteamAuthApi>(PlayServSteamAuth.Api);
            context.Services.Register(PlayServSteamAuth.Api);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
