#if !PLAYSERV_DISABLE_EVENTS
using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
using Playserv.Server;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiEventsFacade
    {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
        private readonly IPlayServLocalExecution _localExecution;
#endif
        private readonly Func<PlayServImplementation> _getCurrentInstance;
        private readonly Func<string, PlayServImplementation> _getInstanceForFireAndForget;
        private readonly Func<PlayServImplementation> _getRequiredInstance;

        public PlayServApiEventsFacade(
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            IPlayServLocalExecution localExecution,
#endif
            Func<PlayServImplementation> getCurrentInstance,
            Func<string, PlayServImplementation> getInstanceForFireAndForget,
            Func<PlayServImplementation> getRequiredInstance)
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            _localExecution = localExecution ?? throw new ArgumentNullException(nameof(localExecution));
#endif
            _getCurrentInstance = getCurrentInstance ?? throw new ArgumentNullException(nameof(getCurrentInstance));
            _getInstanceForFireAndForget = getInstanceForFireAndForget ?? throw new ArgumentNullException(nameof(getInstanceForFireAndForget));
            _getRequiredInstance = getRequiredInstance ?? throw new ArgumentNullException(nameof(getRequiredInstance));
        }

#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE && !PLAYSERV_DISABLE_LOCAL_EXECUTION_SERVER
        public void SetEventHandler(IEventHandler eventHandler)
        {
            _localExecution.SetEventHandler(eventHandler);
        }
#endif

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            return _localExecution.TrySubscribe(onNext, _getCurrentInstance() != null, out var subscription)
                ? subscription
                : _getRequiredInstance().Subscribe(onNext);
#else
            return _getRequiredInstance().Subscribe(onNext);
#endif
        }

        public IObservable<T> Subscribe<T>()
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            return _localExecution.TrySubscribe<T>(_getCurrentInstance() != null, out var observable)
                ? observable
                : _getRequiredInstance().Subscribe<T>();
#else
            return _getRequiredInstance().Subscribe<T>();
#endif
        }

        public void Publish<T>(T @event)
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            if (_localExecution.TryPublish(@event, _getCurrentInstance() != null))
                return;
#endif

            var instance = _getInstanceForFireAndForget("event publish");
            if (instance == null)
                return;

            instance.Publish(@event);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            if (_localExecution.TryPublishForGroup(groupName, @event, _getCurrentInstance() != null))
                return;
#endif

            var instance = _getInstanceForFireAndForget("group event publish");
            if (instance == null)
                return;

            instance.PublishForGroup(groupName, @event);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
#if !PLAYSERV_DISABLE_LOCAL_EXECUTION_CORE
            if (_localExecution.TryPublishForUser(userId, @event, _getCurrentInstance() != null))
                return;
#endif

            var instance = _getInstanceForFireAndForget("user event publish");
            if (instance == null)
                return;

            instance.PublishForUser(userId, @event);
        }

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return _getRequiredInstance().SubscribeGroupAsync(groupName, ct);
        }

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return _getRequiredInstance().UnsubscribeGroupAsync(groupName, ct);
        }
    }
}

#endif
