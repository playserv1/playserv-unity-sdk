using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Server;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiEventsFacade
    {
        private readonly PlayServApiLocalExecutionFacade _localExecution;
        private readonly Func<PlayServImplementation> _getCurrentInstance;
        private readonly Func<string, PlayServImplementation> _getInstanceForFireAndForget;
        private readonly Func<PlayServImplementation> _getRequiredInstance;

        public PlayServApiEventsFacade(
            PlayServApiLocalExecutionFacade localExecution,
            Func<PlayServImplementation> getCurrentInstance,
            Func<string, PlayServImplementation> getInstanceForFireAndForget,
            Func<PlayServImplementation> getRequiredInstance)
        {
            _localExecution = localExecution ?? throw new ArgumentNullException(nameof(localExecution));
            _getCurrentInstance = getCurrentInstance ?? throw new ArgumentNullException(nameof(getCurrentInstance));
            _getInstanceForFireAndForget = getInstanceForFireAndForget ?? throw new ArgumentNullException(nameof(getInstanceForFireAndForget));
            _getRequiredInstance = getRequiredInstance ?? throw new ArgumentNullException(nameof(getRequiredInstance));
        }

        public void SetEventHandler(IEventHandler eventHandler)
        {
            _localExecution.SetEventHandler(eventHandler);
        }

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
            return _localExecution.TrySubscribe(onNext, _getCurrentInstance() != null, out var subscription)
                ? subscription
                : _getRequiredInstance().Subscribe(onNext);
        }

        public IObservable<T> Subscribe<T>()
        {
            return _localExecution.TrySubscribe<T>(_getCurrentInstance() != null, out var observable)
                ? observable
                : _getRequiredInstance().Subscribe<T>();
        }

        public void Publish<T>(T @event)
        {
            if (_localExecution.TryPublish(@event, _getCurrentInstance() != null))
                return;

            var instance = _getInstanceForFireAndForget("event publish");
            if (instance == null)
                return;

            instance.Publish(@event);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            if (_localExecution.TryPublishForGroup(groupName, @event, _getCurrentInstance() != null))
                return;

            var instance = _getInstanceForFireAndForget("group event publish");
            if (instance == null)
                return;

            instance.PublishForGroup(groupName, @event);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            if (_localExecution.TryPublishForUser(userId, @event, _getCurrentInstance() != null))
                return;

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
