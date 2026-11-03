using System;
using Playserv.Modules;
using Playserv.Wrapper;

namespace Playserv.EpicAuth
{
    public sealed class PlayServEpicAuthModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.EpicAuth,
            isCore: false,
            PlayServModuleIds.ClientExecution);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            context.Services.Register<IPlayServEpicAuthApi>(PlayServEpicAuth.Api);
            context.Services.Register(PlayServEpicAuth.Api);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
