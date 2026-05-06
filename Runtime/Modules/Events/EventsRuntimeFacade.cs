using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Modules;

namespace Playserv.Events
{
    internal sealed class EventsRuntimeFacade
    {
        private readonly IPlayServModuleServiceProvider _moduleServices;

        public EventsRuntimeFacade(IPlayServModuleServiceProvider moduleServices)
        {
            _moduleServices = moduleServices ?? throw new ArgumentNullException(nameof(moduleServices));
        }

        public IObservable<T> Subscribe<T>() => EventsAdapter.Subscribe<T>();

        public IDisposable Subscribe<T>(Action<T> onNext) => EventsAdapter.Subscribe(onNext);

        public void Publish<T>(T @event) => EventsAdapter.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) => EventsAdapter.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) => EventsAdapter.PublishForUser(userId, @event);

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            EventsAdapter.SubscribeGroupAsync(groupName, ct);

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>
            EventsAdapter.UnsubscribeGroupAsync(groupName, ct);

        private IEventsAdapter EventsAdapter => _moduleServices.Get<IEventsAdapter>();
    }
}
