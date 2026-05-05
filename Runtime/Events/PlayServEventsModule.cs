#if !PLAYSERV_DISABLE_EVENTS
using System;
using Playserv.Modules;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.Events
{
    public sealed class PlayServEventsModule : IPlayServModule
    {
        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Events,
            isCore: false,
            PlayServModuleIds.Transport,
            PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var adapter = new PlayServEventsAdapter(
                context.Services.Get<ITransport>(),
                context.Services.Get<IJsonCodec>(),
                PlayServLog.ForCategory(PlayServLogCategory.Events));
            context.Services.Register<IEventsAdapter>(adapter);
            context.Services.Register(adapter);
            context.Services.Register(this);
        }

        public void Shutdown()
        {
        }
    }
}

#endif
