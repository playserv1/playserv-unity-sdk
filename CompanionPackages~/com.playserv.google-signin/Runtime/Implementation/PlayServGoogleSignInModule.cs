using System;
using Playserv.Modules;
using Playserv.Wrapper;

namespace Playserv.GoogleSignIn
{
    public sealed class PlayServGoogleSignInModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.GoogleSignIn,
            isCore: false,
            PlayServModuleIds.ClientExecution);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Services.Register<IPlayServGoogleSignInApi>(PlayServGoogleSignIn.Api);
            context.Services.Register(PlayServGoogleSignIn.Api);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
