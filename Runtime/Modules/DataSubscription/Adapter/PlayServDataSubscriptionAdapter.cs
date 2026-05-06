using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class PlayServDataSubscriptionAdapter : IDataSubscriptionAdapter, IDisposable
    {
        internal const int DataGetResponseTimeoutMs = 15000;
        internal const int DataSubscriptionResponseTimeoutMs = 8000;
        internal const int DefaultDataGetPollIntervalMs = 4000;
        internal const int DefaultDataGetPollRequestTimeoutMs = 4000;
        internal const int SubscriptionPollIntervalMs = 3000;
        internal const int SubscriptionPollRequestTimeoutMs = 4000;

        private readonly PlayServImplementation _transport;
        private readonly ILogger _logger;
        private readonly IJsonCodec _jsonCodec;
        private readonly DataSubscriptionRegistry _registry;
        private readonly TransportSubscriptionClient _transportSubscriptionClient;
        private readonly DataMutationClient _dataMutationClient;
        private readonly DataGetClient _dataGetClient;
        private readonly DataSubscriptionPollingCoordinator _pollingCoordinator;

        public PlayServDataSubscriptionAdapter(PlayServImplementation proxy, ILogger logger, IJsonCodec jsonCodec = null)
        {
            _transport = proxy ?? throw new ArgumentNullException(nameof(proxy));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jsonCodec = jsonCodec ?? new NewtonsoftJsonCodec();
            _registry = new DataSubscriptionRegistry(logger, _jsonCodec);
            var requestIds = new DataSubscriptionRequestIdSource();
            _transportSubscriptionClient = new TransportSubscriptionClient(proxy, logger, requestIds, _jsonCodec);
            _dataMutationClient = new DataMutationClient(proxy, requestIds);
            _dataGetClient = new DataGetClient(proxy, logger, requestIds, _jsonCodec);
            _pollingCoordinator = new DataSubscriptionPollingCoordinator(proxy, _dataGetClient, logger);
        }

        internal IJsonCodec GetJsonCodec()
        {
            return _jsonCodec;
        }

        public IDisposable OnSubscriptionData(long subscriptionId, Action<object> onData)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                    onData(dataUpdate.Data);
            });
        }

        public IDisposable OnSubscriptionUpdate(long subscriptionId, Action<DataSubscriptionUpdate> onUpdate)
        {
            return _transport.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                    onUpdate(dataUpdate);
            });
        }

        internal long NextSubscriptionId()
        {
            return _registry.NextSubscriptionId();
        }

        internal Task<long?> TryOpenTransportSubscriptionAsync(
            string query,
            Dictionary<string, object> variables,
            bool allowFallbackToPolling = true,
            CancellationToken ct = default)
        {
            return _transportSubscriptionClient.TryOpenTransportSubscriptionAsync(query, variables, allowFallbackToPolling, ct);
        }

        internal IDisposable RegisterPollingSubscription(
            long subscriptionId,
            string key,
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            Action<object> onChanged,
            Action<DataSubscriptionException> onError = null)
        {
            var entry = _registry.RegisterPollingSubscription(
                subscriptionId,
                key,
                query,
                variables,
                rootFieldName,
                onChanged,
                onError);

            var pollingHandle = _pollingCoordinator.StartSubscriptionPolling(
                entry,
                SubscriptionPollIntervalMs,
                SubscriptionPollRequestTimeoutMs,
                response => _registry.ProcessResponse(subscriptionId, response),
                ex => _registry.ProcessException(subscriptionId, ex));

            _registry.AttachPollingHandle(subscriptionId, pollingHandle);
            return _registry.CreateSubscriptionHandle(subscriptionId, UnregisterSubscription);
        }

        internal Task RefreshSubscriptionAsync(long subscriptionId, CancellationToken ct = default)
        {
            var entry = _registry.GetRequiredEntry(subscriptionId);
            return _pollingCoordinator.RefreshEntryAsync(
                entry,
                SubscriptionPollRequestTimeoutMs,
                ct,
                response => _registry.ProcessResponse(subscriptionId, response),
                ex => _registry.ProcessException(subscriptionId, ex));
        }

        internal void UnregisterSubscription(long subscriptionId)
        {
            _registry.UnregisterSubscription(subscriptionId);
        }

        public void SendMutation(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            _dataMutationClient.SendMutation(subscriptionId, query, variables, patch);
        }

        public Task SendMutationAsync(long subscriptionId, string query, Dictionary<string, object> variables, object patch)
        {
            return _dataMutationClient.SendMutationAsync(subscriptionId, query, variables, patch);
        }

        public void RequestFullState(long subscriptionId)
        {
            _ = RequestFullStateAsync(subscriptionId);
        }

        public Task RequestFullStateAsync(long subscriptionId)
        {
            if (_registry.TryGetEntry(subscriptionId, out var entry))
            {
                return _pollingCoordinator.RefreshEntryAsync(
                    entry,
                    SubscriptionPollRequestTimeoutMs,
                    CancellationToken.None,
                    response => _registry.ProcessResponse(subscriptionId, response),
                    ex => _registry.ProcessException(subscriptionId, ex));
            }

            SafeLogWarning(
                "[DataSubscription] Transport refresh skipped. DataSubscriptionRefreshRequest is disabled for compatibility.");
            return Task.CompletedTask;
        }

        public async Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode)
            where TEntity : class
            where TDto : class, new()
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player id is required.", nameof(playerId));

            if (map == null)
                throw new ArgumentNullException(nameof(map));

            var entityTypeName = typeof(TEntity).Name;
            var query = QueryBuilder.BuildQuery<TEntity>(entityTypeName, playerId, jsonCodec: _jsonCodec);
            var variables = QueryBuilder.BuildVariables(playerId);

            if (mode == DataSubscriptionMode.Transport)
            {
                var transportSubscriptionId = await TryOpenTransportSubscriptionAsync(
                    query,
                    variables,
                    allowFallbackToPolling: false);

                if (transportSubscriptionId.HasValue)
                {
                    SafeLog(
                        $"[DataSubscription] Opened transport subscription. id={transportSubscriptionId.Value}, key={playerId}");

                    return new SharedEntity<TDto>(
                        this,
                        transportSubscriptionId.Value,
                        null,
                        query,
                        variables,
                        raw =>
                        {
                            if (raw == null)
                                return new TDto();

                            var entity = _jsonCodec.Convert<TEntity>(raw);
                            return entity == null ? new TDto() : map(entity);
                        });
                }

                throw new DataSubscriptionException(
                    0,
                    "Transport subscription failed and fallback is disabled. Use polling subscription explicitly.");
            }

            SafeLog(
                $"[DataSubscription] Opened polling subscription. entity={entityTypeName}, key={playerId}, query={query}");

            var subscriptionId = NextSubscriptionId();
            return new SharedEntity<TDto>(
                this,
                subscriptionId,
                playerId,
                entityTypeName,
                null,
                query,
                variables,
                raw =>
                {
                    if (raw == null)
                        return new TDto();

                    var entity = _jsonCodec.Convert<TEntity>(raw);
                    return entity == null ? new TDto() : map(entity);
                });
        }

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            return _dataGetClient.GetDataByKeyAsync(key, query, variables, DataGetResponseTimeoutMs, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return _pollingCoordinator.StartDataByKeyPolling(
                key,
                query,
                variables,
                DefaultDataGetPollIntervalMs,
                DefaultDataGetPollRequestTimeoutMs,
                onData,
                onError);
        }

        public void Dispose()
        {
            _registry.DisposeAll();
        }

        private void SafeLog(string message)
        {
            try
            {
                _logger.Log(message);
            }
            catch
            {
            }
        }

        private void SafeLogWarning(string message)
        {
            try
            {
                _logger.LogWarning(message);
            }
            catch
            {
            }
        }
    }
}
