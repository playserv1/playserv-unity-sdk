using System;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;

namespace Playserv.DataSubscription
{
    public sealed class PlayServDataSubscriptionModule : IPlayServModule, IPlayServConnectionAwareModule
    {
        private PlayServDataSubscriptionAdapter _adapter;

        public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor(
            PlayServModuleIds.Data,
            isCore: false,
            PlayServModuleIds.Transport,
            PlayServModuleIds.Serialization);

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            CommandTypeProviderRegistry.Register(new DataSubscriptionCommandTypeProvider());

            _adapter = new PlayServDataSubscriptionAdapter(
                context.Services.Get<IPlayServCommandBus>(),
                PlayServLog.ForCategory(PlayServLogCategory.Data));
            context.Services.Register<IDataSubscriptionAdapter>(_adapter);
            context.Services.Register(_adapter);

            context.Services.Register(this);
        }

        public void Shutdown()
        {
            _adapter?.Dispose();
            _adapter = null;
        }

        public void OnConnected()
        {
            _adapter?.OnConnected();
        }
    }
}
