using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Events;
using Playserv.RPC;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;

namespace Playserv.Proxy.Common
{
    public sealed partial class PlayServImplementation : IDisposable
    {
        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly PlayServEventsAdapter _eventsAdapter;
        private readonly PlayServDataSubscriptionAdapter _dataSubscriptionAdapter;
        private readonly PlayServTransportSession _transportSession;

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
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            if (requestIdGenerator == null)
                throw new ArgumentNullException(nameof(requestIdGenerator));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            _logger = logger;

            if (transportImplementationFactory == null)
                transportImplementationFactory = ep => TransportImplementationResolver.Create(new TransportModuleContext(ep, logger));

            var implementation = transportImplementationFactory(endpoint);

            _transport = new Transport(implementation, serializer, requestIdGenerator, logger);
            _eventsAdapter = new PlayServEventsAdapter(_transport, PlayServLog.ForCategory(PlayServLogCategory.Events));
            _dataSubscriptionAdapter = new PlayServDataSubscriptionAdapter(this, PlayServLog.ForCategory(PlayServLogCategory.Data));
            _transportSession = new PlayServTransportSession(
                _transport,
                _logger,
                SynchronizationContext.Current,
                error => OnTransportError?.Invoke(error),
                () => OnKeepAlivePingSent?.Invoke(),
                () => OnKeepAlivePongReceived?.Invoke(),
                response => OnRpcInvokeResponse?.Invoke(response),
                InitializeSpawnManager);

            SetupCommandHandlers();
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

        public IDisposable On<T>(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            return On<T>().Subscribe(onNext);
        }

        public void Send<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            _ = _transport.Send(command, moduleName);
        }

        public async Task SendAsync<T>(T command, string moduleName = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            await _transport.Send(command, moduleName);
        }

        public IObservable<T> On<T>()
        {
            return _transport.OnReceive<T>();
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
            if (_transport is Transport transport)
            {
                return transport.GetImplementation();
            }
            return null;
        }

        internal ITransport GetTransport()
        {
            return _transport;
        }

        internal ILogger GetLogger()
        {
            return _logger;
        }

        internal PlayServDataSubscriptionAdapter GetDataSubscriptionAdapter()
        {
            return _dataSubscriptionAdapter;
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new()
        {
            return _dataSubscriptionAdapter.SelectEntity(playerId, map, mode);
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
            return _dataSubscriptionAdapter.StartDataByKeyPolling(
                key,
                query,
                variables,
                onData,
                onError);
        }

        private void SetupCommandHandlers()
        {
            OnCommand("error", OnCommandErrorReceived);
            OnCommand("CommandErrorResponse", OnCommandErrorReceived);
            OnCommand("RpcErrorResponse", OnCommandErrorReceived);
            OnCommand("rpc.RpcErrorResponse", OnCommandErrorReceived);
            OnCommand("Disconnect", OnDisconnectReceived);
            OnCommand("ForcedDisconnect", OnForcedDisconnectReceived);
            OnCommand("module_proxy.ForcedDisconnect", OnForcedDisconnectReceived);
            OnCommand("ClientSettingsResponse", OnClientSettingsResponseReceived);
            OnCommand("ParseErrorResponse", OnParseErrorReceived);
            OnCommand("ValidationErrorResponse", OnValidationErrorReceived);
            OnCommand("InvokeRpcResponse", OnInvokeRpcResponseReceived);
            OnCommand("rpc.InvokeRpcResponse", OnInvokeRpcResponseReceived);
            OnCommand("rpc.InvokeRpc.InvokeRpcResponse", OnInvokeRpcResponseReceived);
        }

        private void OnDisconnectReceived(object command) => _transportSession.HandleDisconnect();

        private void OnForcedDisconnectReceived(object command)
        {
            var response = command as ForcedDisconnectResponse;
            _transportSession.HandleForcedDisconnect(response);
        }

        private void OnClientSettingsResponseReceived(object command)
        {
            if (command is ClientSettingsResponse response)
            {
                _transportSession.HandleClientSettingsResponse(response);
            }
            else
            {
                _logger.LogWarning($"Received ClientSettingsResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private void OnParseErrorReceived(object command)
        {
            if (command is ParseErrorResponse response)
            {
                _logger.LogError($"Parse error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
            }
            else
            {
                _logger.LogWarning($"Received ParseErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private void OnValidationErrorReceived(object command)
        {
            if (command is ValidationErrorResponse response)
            {
                _logger.LogError($"Validation error received from server. Error: {response.Error}, Received JSON: {response.ReceivedJson}");
            }
            else
            {
                _logger.LogWarning($"Received ValidationErrorResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private void OnCommandErrorReceived(object command)
        {
            if (command is CommandErrorResponse response)
            {
                if (IsUnsupportedDataSubscriptionRefresh(response))
                {
                    _logger.LogWarning(
                        $"Server command warning received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
                    return;
                }

                _logger.LogError(
                    $"Server command error received. Error: {response.Error}, Message: {response.Message}, Timestamp: {response.Timestamp}");
            }
            else
            {
                _logger.LogWarning(
                    $"Received 'error' command with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        private static bool IsUnsupportedDataSubscriptionRefresh(CommandErrorResponse response)
        {
            var message = response?.Message ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("DataSubscriptionRefreshRequest", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   message.IndexOf("not supported by this module", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnInvokeRpcResponseReceived(object command)
        {
            if (command is InvokeRpcResponse response)
            {
                _transportSession.HandleInvokeRpcResponse(response);
            }
            else
            {
                _logger.LogWarning(
                    $"[PlayServ][RPC] Received InvokeRpcResponse with unexpected payload type: {command?.GetType().Name ?? "null"}");
            }
        }

        public void Dispose()
        {
            DisposeSpawnManager();

            _dataSubscriptionAdapter.Dispose();

            _transportSession.Dispose();

            _transport.Dispose();
        }
    }
}
