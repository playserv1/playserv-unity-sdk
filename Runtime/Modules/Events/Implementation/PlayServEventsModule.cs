using System;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Serialization;

namespace Playserv.Events
{
    public sealed class PlayServEventsModule : IPlayServModule, IPlayServConnectionAwareModule
    {
        private PlayServEventsAdapter _adapter;

        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Events,
            isCore: false,
            PlayServModuleIds.Transport,
            PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            CommandTypeProviderRegistry.Register(new EventCommandTypeProvider());

            var adapter = new PlayServEventsAdapter(
                context.Services.Get<ITransport>(),
                context.Services.Get<IJsonCodec>(),
                PlayServLog.ForCategory(PlayServLogCategory.Events));
            context.Services.Register<IEventsAdapter>(adapter);
            context.Services.Register(adapter);
            context.Services.Register(this);
            _adapter = adapter;
        }

        // Fires on every established connection, including transparent reconnects. Server-side
        // event/group subscriptions die with the old connection while local observers live on —
        // replay them so broadcasts survive a reconnect (first connect: nothing tracked, no-op).
        public void OnConnected()
        {
            _adapter?.ResubscribeAllOnReconnect();
        }

        public void Shutdown()
        {
        }
    }
}
