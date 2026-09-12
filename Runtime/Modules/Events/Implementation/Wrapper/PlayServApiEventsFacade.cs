using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Events;
using Playserv.Modules;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiEventsFacade : IPlayServEventsApi
    {
        private readonly IPlayServEventsRuntimeAccess _runtimeAccess;

        public PlayServApiEventsFacade()
            : this(new PlayServEventsRuntimeAccess())
        {
        }

        public PlayServApiEventsFacade(IPlayServEventsRuntimeAccess runtimeAccess)
        {
            _runtimeAccess = runtimeAccess ?? throw new ArgumentNullException(nameof(runtimeAccess));
        }

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
            return _runtimeAccess.LocalExecution.TrySubscribe(onNext, _runtimeAccess.HasCurrentInstance, out var subscription)
                ? subscription
                : EventsAdapter.Subscribe(onNext);
        }

        public IDisposable SubscribeRaw<T>(Action<string> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return EventsAdapter.SubscribeRaw<T>(onNext);
        }

        public IObservable<T> Subscribe<T>()
        {
            return _runtimeAccess.LocalExecution.TrySubscribe<T>(_runtimeAccess.HasCurrentInstance, out var observable)
                ? observable
                : EventsAdapter.Subscribe<T>();
        }

        public IObservable<string> SubscribeRaw<T>()
        {
            return EventsAdapter.SubscribeRaw<T>();
        }

        public void Publish<T>(T @event)
        {
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));
            if (_runtimeAccess.LocalExecution.TryPublish(@event, _runtimeAccess.HasCurrentInstance))
                return;
            throw PlayServEventPublishingException.Broadcast();
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name is required.", nameof(groupName));
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));
            if (_runtimeAccess.LocalExecution.TryPublishForGroup(groupName, @event, _runtimeAccess.HasCurrentInstance))
                return;
            throw PlayServEventPublishingException.Group(groupName.Trim());
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User id is required.", nameof(userId));
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));
            if (_runtimeAccess.LocalExecution.TryPublishForUser(userId, @event, _runtimeAccess.HasCurrentInstance))
                return;
            throw PlayServEventPublishingException.User(userId.Trim());
        }

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return EventsAdapter.SubscribeGroupAsync(groupName, ct);
        }

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return EventsAdapter.UnsubscribeGroupAsync(groupName, ct);
        }

        private IEventsAdapter EventsAdapter => GetEventsAdapter(_runtimeAccess.RequiredServices);

        private static IEventsAdapter GetEventsAdapter(IPlayServModuleServiceProvider services)
        {
            if (services == null)
                throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

            return services.Get<IEventsAdapter>();
        }
    }
}
