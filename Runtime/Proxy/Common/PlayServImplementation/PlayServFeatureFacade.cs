using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Events;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;

namespace Playserv.Proxy.Common
{
    internal sealed class PlayServFeatureFacade : IDisposable
    {
        private readonly ITransport _transport;
        private readonly PlayServEventsAdapter _eventsAdapter;
        private readonly PlayServDataSubscriptionAdapter _dataSubscriptionAdapter;

        public PlayServFeatureFacade(
            ITransport transport,
            PlayServEventsAdapter eventsAdapter,
            PlayServDataSubscriptionAdapter dataSubscriptionAdapter)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _eventsAdapter = eventsAdapter ?? throw new ArgumentNullException(nameof(eventsAdapter));
            _dataSubscriptionAdapter = dataSubscriptionAdapter ?? throw new ArgumentNullException(nameof(dataSubscriptionAdapter));
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

        public IObservable<T> Subscribe<T>()
        {
            return _eventsAdapter.Subscribe<T>();
        }

        public IDisposable Subscribe<T>(Action<T> onNext)
        {
            return _eventsAdapter.Subscribe(onNext);
        }

        public void Publish<T>(T @event)
        {
            _eventsAdapter.Publish(@event);
        }

        public void PublishForGroup<T>(string groupName, T @event)
        {
            _eventsAdapter.PublishForGroup(groupName, @event);
        }

        public void PublishForUser<T>(string userId, T @event)
        {
            _eventsAdapter.PublishForUser(userId, @event);
        }

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return _eventsAdapter.SubscribeGroupAsync(groupName, ct);
        }

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default)
        {
            return _eventsAdapter.UnsubscribeGroupAsync(groupName, ct);
        }

        public ITransportImplementation GetTransportImplementation()
        {
            var transport = _transport as Transport;
            return transport != null ? transport.GetImplementation() : null;
        }

        public ITransport GetTransport()
        {
            return _transport;
        }

        public PlayServDataSubscriptionAdapter GetDataSubscriptionAdapter()
        {
            return _dataSubscriptionAdapter;
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode)
            where TEntity : class
            where TDto : class, new()
        {
            return _dataSubscriptionAdapter.SelectEntity<TEntity, TDto>(playerId, map, mode);
        }

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            return _dataSubscriptionAdapter.GetDataByKeyAsync(key, query, variables, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return _dataSubscriptionAdapter.StartDataByKeyPolling(key, query, variables, onData, onError);
        }

        public void Dispose()
        {
            _dataSubscriptionAdapter.Dispose();
            _transport.Dispose();
        }
    }
}
