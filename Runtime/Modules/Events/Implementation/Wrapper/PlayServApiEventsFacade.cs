using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Events;
using Playserv.Modules;
using Playserv.Proxy.Common;
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
using Playserv.Server;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiEventsFacade : IPlayServEventsApi
    {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
        private readonly IPlayServLocalExecution _localExecution;
#endif
        private readonly Func<PlayServImplementation> _getCurrentInstance;
        private readonly Func<string, PlayServImplementation> _getInstanceForFireAndForget;
        private readonly Func<PlayServImplementation> _getRequiredInstance;

        public PlayServApiEventsFacade()
            : this(
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
                (IPlayServLocalExecution)PlayServRuntimeHost.LocalExecution,
#endif
                () => PlayServRuntimeHost.CurrentInstance,
                PlayServRuntimeHost.GetInstanceForFireAndForget,
                () => PlayServRuntimeHost.RequiredInstance)
        {
        }

        public PlayServApiEventsFacade(
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            IPlayServLocalExecution localExecution,
#endif
            Func<PlayServImplementation> getCurrentInstance,
            Func<string, PlayServImplementation> getInstanceForFireAndForget,
            Func<PlayServImplementation> getRequiredInstance)
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            _localExecution = localExecution ?? throw new ArgumentNullException(nameof(localExecution));
#endif
            _getCurrentInstance = getCurrentInstance ?? throw new ArgumentNullException(nameof(getCurrentInstance));
            _getInstanceForFireAndForget = getInstanceForFireAndForget ?? throw new ArgumentNullException(nameof(getInstanceForFireAndForget));
            _getRequiredInstance = getRequiredInstance ?? throw new ArgumentNullException(nameof(getRequiredInstance));
        }

#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE && !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_SERVER
        public void SetEventHandler(IEventHandler eventHandler)
        {
            _localExecution.SetEventHandler(eventHandler);
        }
#endif

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            return _localExecution.TrySubscribe(onNext, _getCurrentInstance() != null, out var subscription)
                ? subscription
                : EventsAdapter.Subscribe(onNext);
#else
            return EventsAdapter.Subscribe(onNext);
#endif
        }

        public IObservable<T> Subscribe<T>()
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            return _localExecution.TrySubscribe<T>(_getCurrentInstance() != null, out var observable)
                ? observable
                : EventsAdapter.Subscribe<T>();
#else
            return EventsAdapter.Subscribe<T>();
#endif
        }

        public void Publish<T>(T @event)
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            if (_localExecution.TryPublish(@event, _getCurrentInstance() != null))
                return;
#endif

            var instance = _getInstanceForFireAndForget("event publish");
            if (instance == null)
                return;

            GetEventsAdapter(instance).Publish(@event);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            if (_localExecution.TryPublishForGroup(groupName, @event, _getCurrentInstance() != null))
                return;
#endif

            var instance = _getInstanceForFireAndForget("group event publish");
            if (instance == null)
                return;

            GetEventsAdapter(instance).PublishForGroup(groupName, @event);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
#if !PLAYSERV_MODULE_DISABLED_LOCAL_EXECUTION_CORE
            if (_localExecution.TryPublishForUser(userId, @event, _getCurrentInstance() != null))
                return;
#endif

            var instance = _getInstanceForFireAndForget("user event publish");
            if (instance == null)
                return;

            GetEventsAdapter(instance).PublishForUser(userId, @event);
        }

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return EventsAdapter.SubscribeGroupAsync(groupName, ct);
        }

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return EventsAdapter.UnsubscribeGroupAsync(groupName, ct);
        }

        private IEventsAdapter EventsAdapter => GetEventsAdapter(_getRequiredInstance());

        private static IEventsAdapter GetEventsAdapter(PlayServImplementation instance)
        {
            if (instance == null)
                throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

            return instance.ModuleServices.Get<IEventsAdapter>();
        }
    }
}
