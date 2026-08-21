using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class PlayServDataSubscriptionAdapter : IDataSubscriptionAdapter, IDisposable
    {
        internal const int DataGetResponseTimeoutMs = 15000;
        internal const int DataSubscriptionResponseTimeoutMs = 8000;
        internal const int DataMutationResponseTimeoutMs = 8000;
        internal const int DataSubscriptionRefreshTimeoutMs = 8000;
        internal const int DataSubscriptionCloseTimeoutMs = 8000;
        internal const int DefaultDataGetPollIntervalMs = 4000;
        internal const int DefaultDataGetPollRequestTimeoutMs = 4000;
        internal const int SubscriptionPollIntervalMs = 3000;
        internal const int SubscriptionPollRequestTimeoutMs = 4000;

        private readonly IPlayServCommandBus _commandBus;
        private readonly ILogger _logger;
        private readonly IJsonCodec _jsonCodec;
        private readonly DataSubscriptionRegistry _registry;
        private readonly TransportSubscriptionClient _transportSubscriptionClient;
        private readonly DataSubscriptionCloseClient _transportCloseClient;
        private readonly TransportSubscriptionRegistry _transportRegistry;
        private readonly DataSubscriptionRefreshClient _transportRefreshClient;
        private readonly DataMutationClient _dataMutationClient;
        private readonly DataGetClient _dataGetClient;
        private readonly DataSubscriptionPollingCoordinator _pollingCoordinator;

        public PlayServDataSubscriptionAdapter(IPlayServCommandBus commandBus, ILogger logger, IJsonCodec jsonCodec = null)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jsonCodec = jsonCodec ?? new NewtonsoftJsonCodec();
            _registry = new DataSubscriptionRegistry(logger, _jsonCodec);
            var requestIds = new DataSubscriptionRequestIdSource();
            _transportSubscriptionClient = new TransportSubscriptionClient(commandBus, logger, requestIds, _jsonCodec);
            _transportCloseClient = new DataSubscriptionCloseClient(commandBus, requestIds, _jsonCodec);
            _transportRegistry = new TransportSubscriptionRegistry(
                commandBus,
                _transportSubscriptionClient,
                _transportCloseClient,
                _jsonCodec,
                logger);
            _transportRefreshClient = new DataSubscriptionRefreshClient(commandBus, logger, requestIds, _jsonCodec);
            _dataMutationClient = new DataMutationClient(commandBus, logger, requestIds, _jsonCodec);
            _dataGetClient = new DataGetClient(commandBus, logger, requestIds, _jsonCodec);
            _pollingCoordinator = new DataSubscriptionPollingCoordinator(commandBus, _dataGetClient, logger);
        }

        internal IJsonCodec GetJsonCodec()
        {
            return _jsonCodec;
        }

        internal bool CanSendCommands => _commandBus.State == PlayServState.Online;

        internal DataSubscriptionException CreateConnectionUnavailableException(string operationName)
        {
            var operation = string.IsNullOrWhiteSpace(operationName) ? "Data subscription operation" : operationName;
            return new DataSubscriptionException(
                0,
                $"{operation} skipped because the connection is unavailable (SDK state is {_commandBus.State}).",
                retryable: true,
                sourceCode: "subscription_connection_unavailable");
        }

        public IDisposable OnSubscriptionData(long subscriptionId, Action<object> onData)
        {
            return _commandBus.On<DataSubscriptionUpdate>(update =>
            {
                if (update is DataSubscriptionUpdate dataUpdate && dataUpdate.DataSubscriptionId == subscriptionId)
                    onData(dataUpdate.Data);
            });
        }

        public IDisposable OnSubscriptionUpdate(long subscriptionId, Action<DataSubscriptionUpdate> onUpdate)
        {
            return _commandBus.On<DataSubscriptionUpdate>(update =>
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

        internal Task<TransportSubscriptionLease> AcquireTransportSubscriptionAsync(
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            CancellationToken ct = default)
        {
            return _transportRegistry.AcquireAsync(query, variables, rootFieldName, ct);
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

        internal void SendMutation(
            TransportSubscriptionLease lease,
            string query,
            Dictionary<string, object> variables,
            object patch)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            SendMutation(lease.SubscriptionId, query, variables, patch);
        }

        internal Task SendMutationAsync(
            TransportSubscriptionLease lease,
            string query,
            Dictionary<string, object> variables,
            object patch)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            return SendMutationAsync(lease.SubscriptionId, query, variables, patch);
        }

        public void RequestFullState(long subscriptionId)
        {
            _ = RequestFullStateFireAndForgetAsync(subscriptionId);
        }

        public Task RequestFullStateAsync(long subscriptionId)
        {
            return RequestFullStateAsync(subscriptionId, CancellationToken.None);
        }

        internal Task RequestFullStateAsync(long subscriptionId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_registry.TryGetEntry(subscriptionId, out var entry))
            {
                return _pollingCoordinator.RefreshEntryAsync(
                    entry,
                    SubscriptionPollRequestTimeoutMs,
                    ct,
                    response => _registry.ProcessResponse(subscriptionId, response),
                    ex => _registry.ProcessException(subscriptionId, ex));
            }

            return _transportRefreshClient.RefreshAsync(
                subscriptionId,
                DataSubscriptionRefreshTimeoutMs,
                ct);
        }

        internal void RequestFullState(TransportSubscriptionLease lease)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            RequestFullState(lease.SubscriptionId);
        }

        internal Task RequestFullStateAsync(
            TransportSubscriptionLease lease,
            CancellationToken ct = default)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            return RequestFullStateAsync(lease.SubscriptionId, ct);
        }

        internal Task<DataSubscriptionUpdate> RequestTransportFullStateAsync(
            TransportSubscriptionLease lease,
            CancellationToken ct = default,
            Action<long> onRequestCreated = null)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            ct.ThrowIfCancellationRequested();
            return _transportRefreshClient.RefreshWithUpdateAsync(
                lease.SubscriptionId,
                DataSubscriptionRefreshTimeoutMs,
                ct,
                onRequestCreated);
        }

        private async Task RequestFullStateFireAndForgetAsync(long subscriptionId)
        {
            try
            {
                await RequestFullStateAsync(subscriptionId);
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[DataSubscription] Full state refresh skipped: {ex.Message}");
            }
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
                var transportLease = await AcquireTransportSubscriptionAsync(
                    query,
                    variables,
                    entityTypeName);

                SafeLog(
                    $"[DataSubscription] Opened transport subscription. id={transportLease.SubscriptionId}, key={playerId}");

                return new SharedEntity<TDto>(
                    this,
                    transportLease,
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

        public async Task<ISharedCollection<TItem>> SelectCollection<TItem>(
            string query,
            Dictionary<string, object> variables = null)
            where TItem : class, new()
        {
            return await SelectTypedCollectionAsync<TItem>(query, variables, CancellationToken.None);
        }

        internal async Task<ISharedCollection<TItem>> SelectTypedCollectionAsync<TItem>(
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Collection query is required.", nameof(query));

            var rootField = ExtractRootField(query);
            var vars = variables ?? new Dictionary<string, object>();

            // Collections ride the transport (push) plane only — there is no polling collection mode.
            var transportLease = await AcquireTransportSubscriptionAsync(
                query,
                vars,
                rootField,
                ct);

            SafeLog(
                $"[DataSubscription] Opened transport collection. id={transportLease.SubscriptionId}, root={rootField}, query={query}");

            return new SharedCollection<TItem>(this, transportLease, rootField);
        }

        internal async Task<IPlayServRecordSubscription<TItem>> SelectTypedRecordAsync<TItem>(
            PlayServRecord<TItem> record,
            string query,
            Dictionary<string, object> variables,
            string rootFieldName,
            CancellationToken ct)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Record subscription query is required.", nameof(query));

            var transportLease = await AcquireTransportSubscriptionAsync(
                query,
                variables ?? new Dictionary<string, object>(),
                rootFieldName,
                ct);
            var handle = new PlayServRecordSubscription<TItem>(record, this, transportLease);
            try
            {
                await handle.InitializeAsync(ct);
                SafeLog(
                    $"[DataSubscription] Opened typed record subscription. id={transportLease.SubscriptionId}, record={record.Id}");
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        /// <summary>The root field of a query is its leading identifier — the entity/table name
        /// (e.g. <c>Leaderboard</c> in <c>Leaderboard(where: { … }) { … }</c>) — which is the key the
        /// collection array rides under in the server's update frame.</summary>
        private static string ExtractRootField(string query)
        {
            var s = query.TrimStart();
            int i = 0;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                i++;
            return i == 0 ? query.Trim() : s.Substring(0, i);
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
            _transportRegistry.Dispose();
            _registry.DisposeAll();
        }

        internal void OnConnected()
        {
            _transportRegistry.OnConnected();
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
