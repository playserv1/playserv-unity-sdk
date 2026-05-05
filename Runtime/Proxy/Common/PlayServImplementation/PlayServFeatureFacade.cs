using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
#endif
#if !PLAYSERV_DISABLE_EVENTS
using Playserv.Events;
#endif
using Playserv.Modules;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    internal sealed class PlayServFeatureFacade : IDisposable
    {
        private readonly ITransport _transport;
        private readonly IPlayServModuleServiceProvider _moduleServices;

        public PlayServFeatureFacade(
            ITransport transport,
            IPlayServModuleServiceProvider moduleServices)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _moduleServices = moduleServices ?? throw new ArgumentNullException(nameof(moduleServices));
        }

        public IDisposable On<T>(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return On<T>().Subscribe(onNext);
        }

        public IObservable<T> On<T>()
        {
            return _transport.OnReceive<T>();
        }

        public void Send<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            _ = _transport.Send(command, moduleName);
        }

        public Task SendAsync<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            return _transport.Send(command, moduleName);
        }

        public IObservable<object> OnCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be null or empty.", nameof(commandName));

            return _transport.OnReceive(commandName);
        }

        public IDisposable OnCommand(string commandName, Action<object> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return OnCommand(commandName).Subscribe(onNext);
        }

#if !PLAYSERV_DISABLE_EVENTS
        public IObservable<T> Subscribe<T>()
        {
            return EventsAdapter.Subscribe<T>();
        }

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
            return EventsAdapter.Subscribe(onNext);
        }

        public void Publish<T>(T @event)
        {
            EventsAdapter.Publish(@event);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            EventsAdapter.PublishForGroup(groupName, @event);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            EventsAdapter.PublishForUser(userId, @event);
        }

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return EventsAdapter.SubscribeGroupAsync(groupName, ct);
        }

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return EventsAdapter.UnsubscribeGroupAsync(groupName, ct);
        }
#endif

        public ITransportImplementation GetTransportImplementation()
        {
            var transport = _transport as Transport;
            return transport != null ? transport.GetImplementation() : null;
        }

        public ITransport GetTransport()
        {
            return _transport;
        }

#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
        public PlayServDataSubscriptionAdapter GetDataSubscriptionAdapter()
        {
            return _moduleServices.Get<PlayServDataSubscriptionAdapter>();
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode)
            where TEntity : class
            where TDto : class, new()
        {
            return DataSubscriptionAdapter.SelectEntity<TEntity, TDto>(playerId, map, mode);
        }

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            return DataSubscriptionAdapter.GetDataByKeyAsync(key, query, variables, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return DataSubscriptionAdapter.StartDataByKeyPolling(key, query, variables, onData, onError);
        }
#endif

        public void Dispose()
        {
            _transport.Dispose();
        }

#if !PLAYSERV_DISABLE_EVENTS
        private IEventsAdapter EventsAdapter => _moduleServices.Get<IEventsAdapter>();
#endif

#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
        private IDataSubscriptionAdapter DataSubscriptionAdapter => _moduleServices.Get<IDataSubscriptionAdapter>();
#endif
    }
}
