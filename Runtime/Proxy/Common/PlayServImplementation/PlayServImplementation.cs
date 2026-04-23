using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.RPC;

namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation : IDisposable
    {
        private readonly ILogger _logger;
        private readonly PlayServFeatureFacade _featureFacade;
        private readonly PlayServTransportSession _transportSession;
        private readonly PlayServCommandRouter _commandRouter;

        public PlayServState State => _transportSession.State;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;
        public event Action<InvokeRpcResponse> OnRpcInvokeResponse;

        public PlayServImplementation(string endpoint)
            : this(endpoint, new JsonSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport)) { }

        internal PlayServImplementation(
            string endpoint,
            Func<string, ITransportImplementation> transportImplementationFactory)
            : this(endpoint, new JsonSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport), transportImplementationFactory) { }

        private PlayServImplementation(
            string endpoint,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ILogger logger,
            Func<string, ITransportImplementation> transportImplementationFactory = null)
        {
            var components = PlayServInstanceFactory.Create(
                this,
                endpoint,
                serializer,
                requestIdGenerator,
                logger,
                transportImplementationFactory,
                SynchronizationContext.Current,
                error => OnTransportError?.Invoke(error),
                () => OnKeepAlivePingSent?.Invoke(),
                () => OnKeepAlivePongReceived?.Invoke(),
                response => OnRpcInvokeResponse?.Invoke(response),
                InitializeSpawnManager);

            _logger = components.Logger;
            _featureFacade = new PlayServFeatureFacade(
                components.Transport,
                components.EventsAdapter,
                components.DataSubscriptionAdapter);
            _transportSession = components.TransportSession;
            _commandRouter = new PlayServCommandRouter(_featureFacade, _transportSession, _logger);
        }

        public void SetConfig(
            string gameAccessToken,
            string gameId,
            string userId,
            string gameVersion,
            string sdkVersion = null,
            bool allowMultipleConnections = true,
            int keepAlivePingIntervalMs = 30000,
            int keepAlivePongTimeoutMs = 10000)
            => _transportSession.Configure(
                gameAccessToken,
                gameId,
                userId,
                gameVersion,
                sdkVersion,
                allowMultipleConnections,
                keepAlivePingIntervalMs,
                keepAlivePongTimeoutMs);

        public Task<bool> Connect() => _transportSession.ConnectAsync();

        public IDisposable On<T>(Action<T> onNext) => _featureFacade.On(onNext);

        public void Send<T>(T command, string moduleName = null) => _featureFacade.Send(command, moduleName);

        public Task SendAsync<T>(T command, string moduleName = null) => _featureFacade.SendAsync(command, moduleName);

        public IObservable<T> On<T>() => _featureFacade.On<T>();

        public IObservable<object> OnCommand(string commandName) => _featureFacade.OnCommand(commandName);

        public IDisposable OnCommand(string commandName, Action<object> onNext) => _featureFacade.OnCommand(commandName, onNext);

        public IObservable<T> Subscribe<T>() => _featureFacade.Subscribe<T>();

        public IDisposable Subscribe<T>(Action<T> onNext) => _featureFacade.Subscribe(onNext);

        public void Publish<T>(T @event) => _featureFacade.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) => _featureFacade.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) => _featureFacade.PublishForUser(userId, @event);

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) => _featureFacade.SubscribeGroupAsync(groupName, ct);

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) => _featureFacade.UnsubscribeGroupAsync(groupName, ct);

        public ITransportImplementation GetTransportImplementation() => _featureFacade.GetTransportImplementation();

        internal ITransport GetTransport() => _featureFacade.GetTransport();

        internal ILogger GetLogger() => _logger;

        internal PlayServDataSubscriptionAdapter GetDataSubscriptionAdapter() => _featureFacade.GetDataSubscriptionAdapter();

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new()
        {
            return _featureFacade.SelectEntity<TEntity, TDto>(playerId, map, mode);
        }

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            return _featureFacade.GetDataByKeyAsync(key, query, variables, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return _featureFacade.StartDataByKeyPolling(key, query, variables, onData, onError);
        }

        public void Dispose()
        {
            _commandRouter.Dispose();
            DisposeSpawnManager();
            _transportSession.Dispose();
            _featureFacade.Dispose();
        }
    }
}
