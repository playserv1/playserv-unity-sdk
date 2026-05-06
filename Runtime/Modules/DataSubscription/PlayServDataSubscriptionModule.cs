#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;

namespace Playserv.DataSubscription
{
    public sealed class PlayServDataSubscriptionModule : IPlayServModule
    {
        private IDataSubscriptionAdapter _adapter;

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
                context.Services.Get<PlayServImplementation>(),
                PlayServLog.ForCategory(PlayServLogCategory.Data));
            context.Services.Register<IDataSubscriptionAdapter>(_adapter);
            if (_adapter is PlayServDataSubscriptionAdapter concreteAdapter)
                context.Services.Register(concreteAdapter);

            context.Services.Register(this);
        }

        public void Shutdown()
        {
            if (_adapter is IDisposable disposable)
                disposable.Dispose();
        }
    }
}

#endif
