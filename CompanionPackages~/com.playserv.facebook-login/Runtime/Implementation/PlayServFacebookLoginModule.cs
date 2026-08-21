using System;
using Playserv.Modules;
using Playserv.Wrapper;

namespace Playserv.FacebookLogin
{
    public sealed class PlayServFacebookLoginModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.FacebookLogin,
            isCore: false,
            PlayServModuleIds.ClientExecution);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            context.Services.Register<IPlayServFacebookLoginApi>(PlayServFacebookLogin.Api);
            context.Services.Register(PlayServFacebookLogin.Api);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
