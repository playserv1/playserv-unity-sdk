using System;
using Playserv.Modules;

namespace Playserv.Pulse
{
    public sealed class PlayServPulseModule : IPlayServModule, IPlayServPulseModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Pulse,
            isCore: false,
            PlayServModuleIds.Transport,
            PlayServModuleIds.Serialization);

        public bool IsImplemented => false;

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Services.Register<IPlayServPulseModule>(this);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}
