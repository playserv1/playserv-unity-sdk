using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
#endif
using Playserv.Modules;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
using Playserv.RPC;
#endif

namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation : IDisposable, IPlayServRuntimeIdentity
    {
        private readonly ILogger _logger;
        private readonly PlayServFeatureFacade _featureFacade;
        private readonly PlayServTransportSession _transportSession;
        private readonly PlayServCommandRouter _commandRouter;
        private readonly PlayServModuleHost _moduleHost;

        public PlayServState State => _transportSession.State;

        public string UserId => _transportSession.UserId;

        public IPlayServModuleServiceProvider ModuleServices => _moduleHost.Services;

        public event Action<TransportError> OnTransportError;
        public event Action OnKeepAlivePingSent;
        public event Action OnKeepAlivePongReceived;
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
        public event Action<InvokeRpcResponse> OnRpcInvokeResponse;
#endif

        public PlayServImplementation(string endpoint)
            : this(endpoint, PlayServJsonCompositionRoot.CreateDefaultSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport)) { }

        internal PlayServImplementation(
            string endpoint,
            PlayServTransportImplementationFactory transportImplementationFactory)
            : this(endpoint, PlayServJsonCompositionRoot.CreateDefaultSerializer(), new RequestIdGenerator(), PlayServLog.ForCategory(PlayServLogCategory.Transport), transportImplementationFactory) { }

        private PlayServImplementation(
            string endpoint,
            IMessageSerializer serializer,
            IRequestIdGenerator requestIdGenerator,
            ILogger logger,
            PlayServTransportImplementationFactory transportImplementationFactory = null)
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
                () => OnKeepAlivePongReceived?.Invoke()
#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_CLIENT_RPC
                ,
                response => OnRpcInvokeResponse?.Invoke(response));
#else
                );
#endif

            _logger = components.Logger;
            _moduleHost = components.ModuleHost;
            _featureFacade = new PlayServFeatureFacade(
                components.Transport,
                components.ModuleHost.Services);
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

#if !PLAYSERV_DISABLE_EVENTS
        public IObservable<T> Subscribe<T>() => _featureFacade.Subscribe<T>();

        public IDisposable Subscribe<T>(Action<T> onNext) => _featureFacade.Subscribe(onNext);

        public void Publish<T>(T @event) => _featureFacade.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) => _featureFacade.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) => _featureFacade.PublishForUser(userId, @event);

        public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) => _featureFacade.SubscribeGroupAsync(groupName, ct);

        public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) => _featureFacade.UnsubscribeGroupAsync(groupName, ct);
#endif

        public ITransportImplementation GetTransportImplementation() => _featureFacade.GetTransportImplementation();

        internal ITransport GetTransport() => _featureFacade.GetTransport();

        internal ILogger GetLogger() => _logger;

        public bool HasModule(string moduleId) => _moduleHost.HasModule(moduleId);

        internal IPlayServModuleServiceProvider GetModuleServices() => ModuleServices;

#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
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
#endif

        public void Dispose()
        {
            _commandRouter.Dispose();
            _moduleHost.Dispose();
            _transportSession.Dispose();
            _featureFacade.Dispose();
        }
    }
}
