using System;
using Playserv.Modules;
using Playserv.Wrapper;

namespace Playserv.AppleSignIn
{
    public sealed class PlayServAppleSignInModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.AppleSignIn,
            isCore: false,
            PlayServModuleIds.ClientExecution);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Services.Register<IPlayServAppleSignInApi>(PlayServAppleSignIn.Api);
            context.Services.Register(PlayServAppleSignIn.Api);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
