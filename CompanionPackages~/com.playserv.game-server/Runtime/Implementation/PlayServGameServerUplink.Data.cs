using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>A dataflow query snapshot. It has no managed-record identity, ETag, or save operation.</summary>
    public sealed class PlayServUplinkDataResult<T>
    {
        internal PlayServUplinkDataResult(string entity, string key, bool found, T value)
        { Entity = entity; Key = key; Found = found; Value = value; }

        public string Entity { get; }
        public string Key { get; }
        /// <summary>False can mean a missing row or a refusal hidden by the backend.</summary>
        public bool Found { get; }
        public T Value { get; }
    }

    /// <summary>
    /// Explicit dataflow operations on one open uplink connection. Reacquire Uplink.Data after
    /// reconnecting. Schema names and dataflow keys are sent unchanged; no connection or replay is implicit.
    /// Cancellation after a send starts does not promise rollback. Mutation completion is not a persistence acknowledgement.
    /// No acting-player attribution is supplied; use player-scoped Records to create player-owned rows.
    /// </summary>
    public sealed class PlayServUplinkData
    {
        private static readonly NewtonsoftJsonCodec Codec = new NewtonsoftJsonCodec();
        private static readonly JsonCodecOptions JsonOptions = new JsonCodecOptions
        { ParseDates = false, IgnoreMetadataProperties = true };
        private readonly PlayServGameServerUplink _uplink;
        private readonly PlayServGameServerUplink.DataConnection _connection;

        internal PlayServUplinkData(PlayServGameServerUplink uplink, PlayServGameServerUplink.DataConnection connection)
        { _uplink = uplink; _connection = connection; }

        /// <summary>
        /// Queries one explicit dataflow key. HttpTimeout bounds catalogue, send and reply; at most
        /// 128 queries may be pending. Found=false does not distinguish absence from a hidden refusal.
        /// Singleton tables accept an empty key; ordinary tables require a non-empty key.
        /// </summary>
        public Task<PlayServUplinkDataResult<T>> QueryAsync<T>(string entityName, string key, CancellationToken ct = default)
        {
            Validate(entityName, key, ct, allowSingletonKey: true);
            var id = Guid.NewGuid().ToString("N");
            var bytes = GameServerUplinkServices.Frame(new { type = "data_query", request_id = id, entity = entityName, id = key });
            return QueryCoreAsync<T>(entityName, key, id, bytes, ct);
        }

        /// <summary>
        /// Sends an object snapshot as data_write/upsert. Completion confirms transmission only, not
        /// persistence. No HTTP fallback, automatic retry or acknowledgement is provided.
        /// Singleton tables accept an empty key. Player-owned creation requiring acting-player attribution is not supported.
        /// </summary>
        public Task SendUpsertAsync<T>(string entityName, string key, T value, CancellationToken ct = default)
        {
            Validate(entityName, key, ct, allowSingletonKey: true);
            if (value == null) throw new ArgumentNullException(nameof(value));
            string snapshot;
            try { snapshot = Codec.Serialize(value, JsonOptions); }
            catch { throw SerializationFailed(); }
            if (string.IsNullOrEmpty(snapshot) || snapshot[0] != '{' || snapshot[snapshot.Length - 1] != '}')
                throw new ArgumentException("The value must serialize as a JSON object.", nameof(value));
            return SendMutationAsync(entityName, key, "upsert", snapshot, false, ct);
        }

        /// <summary>Sends signed 64-bit field deltas. Not supported for singletons; completion means transmission only.</summary>
        public Task SendIncrementAsync(string entityName, string key, IReadOnlyDictionary<string, long> deltas,
            CancellationToken ct = default)
        {
            Validate(entityName, key, ct);
            if (deltas == null) throw new ArgumentNullException(nameof(deltas));
            if (deltas.Count == 0) throw new ArgumentException("At least one field delta is required.", nameof(deltas));
            var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var delta in deltas)
            {
                GameServerServiceValues.RequireText(delta.Key, nameof(deltas));
                snapshot.Add(delta.Key, delta.Value);
            }
            return SendMutationAsync(entityName, key, "increment", Codec.Serialize(snapshot, JsonOptions), true, ct);
        }

        /// <summary>Sends data_write/delete. Not supported for singletons; completion means transmission only.</summary>
        public Task SendDeleteAsync(string entityName, string key, CancellationToken ct = default)
        {
            Validate(entityName, key, ct);
            return SendMutationAsync(entityName, key, "delete", "{}", true, ct);
        }

        private void Validate(string entityName, string key, CancellationToken ct, bool allowSingletonKey = false)
        {
            var context = GameServerUplinkServices.Context(ct);
            GameServerServiceValues.RequireText(entityName, nameof(entityName));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!allowSingletonKey || key.Length != 0) GameServerServiceValues.RequireText(key, nameof(key));
            _uplink.ValidateDataConnection(_connection, context);
        }

        private Task SendMutationAsync(string entityName, string key, string operation, string snapshot,
            bool rejectSingleton, CancellationToken ct)
        {
            var envelope = Codec.Serialize(new { type = "data_write", entity = entityName, op = operation, id = key }, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(envelope.Substring(0, envelope.Length - 1) + ",\"data\":" + snapshot + "}");
            if (bytes.Length > PlayServUplinkSocket.MaxFrameBytes) throw UplinkErrors.Exception("frame_too_large");
            return _uplink.ExecuteDataAsync(_connection, null, bytes,
                token => EnsureAccessAsync(entityName, key, PlayServDataAccessOperation.Write, rejectSingleton, token), ct);
        }

        private async Task<PlayServUplinkDataResult<T>> QueryCoreAsync<T>(string entityName, string key, string id,
            byte[] bytes, CancellationToken ct)
        {
            var json = await _uplink.ExecuteDataAsync(_connection, id, bytes,
                token => EnsureAccessAsync(entityName, key, PlayServDataAccessOperation.Read, false, token), ct).ConfigureAwait(false);
            try
            {
                var body = ParseReply(json);
                if (body == null || !body.TryGetValue("entity", out var entity) || !(entity is string returnedEntity) || returnedEntity != entityName ||
                    !body.TryGetValue("id", out var returnedKey) || !(returnedKey is string textKey) || textKey != key ||
                    !body.TryGetValue("found", out var foundValue) || !(foundValue is bool found) ||
                    !body.TryGetValue("data", out var data) || !(data is IDictionary<string, object>))
                    throw InvalidResult();
                var value = found ? Codec.Deserialize<DataValue<T>>(json, JsonOptions).Value : default;
                return new PlayServUplinkDataResult<T>(entityName, key, found, value);
            }
            catch { throw InvalidResult(); }
        }

        private async Task EnsureAccessAsync(string entityName, string key, PlayServDataAccessOperation operation,
            bool rejectSingleton, CancellationToken ct)
        {
            var client = new PlayServRecordsClient(new PlayServSettings
            { BackendServerAddress = _connection.Context.BackendServerAddress, EnableAutomaticPlayerAuthentication = false },
                new PlayServGameServerRuntimeHttpClient(sendData: (request, token) => _uplink.SendDataCatalogueAsync(_connection, request, token)),
                Codec, requiresClientToken: false, accessSubject: PlayServDataAccessSubject.Server,
                catalogueCacheScope: _connection.CatalogueCacheScope);
            try
            {
                var table = await ResolveTableAsync(client, entityName, false, ct).ConfigureAwait(false);
                if (Capability(client, table, operation) == PlayServDataCapabilityState.Denied)
                {
                    table = await ResolveTableAsync(client, entityName, true, ct).ConfigureAwait(false);
                    if (Capability(client, table, operation) == PlayServDataCapabilityState.Denied)
                        throw new PlayServDataAccessDeniedException("The server catalogue denies this data operation.",
                            table.EntityId, operation, true);
                }
                if (rejectSingleton && table.IsSingleton)
                    throw new InvalidOperationException("Increment and delete are not supported for singleton data.");
                if (key.Length == 0 && !table.IsSingleton)
                    throw new ArgumentException("A non-empty dataflow key is required for ordinary tables.", nameof(key));
            }
            finally
            {
                // A late cancelled reader must not repopulate an ended connection's cache.
                if (_connection.Token.IsCancellationRequested)
                    PlayServRecordsClient.ReleaseCatalogueScope(_connection.CatalogueCacheScope);
            }
        }

        private static PlayServDataCapabilityState Capability(PlayServRecordsClient client, PlayServDataTableInfo table,
            PlayServDataAccessOperation operation)
        {
            var capabilities = client.SelectCapabilities(table.Capabilities);
            return operation == PlayServDataAccessOperation.Read ? capabilities.Read : capabilities.Write;
        }

        private static async Task<PlayServDataTableInfo> ResolveTableAsync(PlayServRecordsClient client, string entityName,
            bool refresh, CancellationToken ct)
        {
            IReadOnlyList<PlayServDataTableInfo> tables;
            try { tables = await client.GetTablesAsync(refresh, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (PlayServDataException error)
            { throw CatalogueFailure(error); }
            catch { throw InvalidResult(); }
            return tables.FirstOrDefault(table => string.Equals(table.Name, entityName, StringComparison.Ordinal)) ??
                throw new PlayServDataTableNotFoundException(entityName);
        }

        internal static IDictionary<string, object> ParseReply(string json)
        {
            try { return Codec.ToPlainValue(Codec.Deserialize<object>(json, JsonOptions), JsonOptions) as IDictionary<string, object>; }
            catch { return null; }
        }
        internal static PlayServGameServerException InvalidResult() => new PlayServGameServerException(
            new PlayServError(PlayServErrorCode.InvalidResponse, "invalid_response", "The uplink data response is invalid."));
        internal static PlayServGameServerException CatalogueFailure(PlayServDataException error) =>
            new PlayServGameServerException(new PlayServError(error.UnifiedError.Code,
                error.UnifiedError.SourceCode, "The server data catalogue refused or could not complete the operation.",
                httpStatus: error.UnifiedError.HttpStatus, retryable: error.UnifiedError.Retryable));
        private static PlayServGameServerException SerializationFailed() => new PlayServGameServerException(
            new PlayServError(PlayServErrorCode.Serialization, "uplink_serialization_failed", "The uplink data could not be serialized."));

        private sealed class DataValue<T>
        {
            [PlayServJsonName("data")] public T Value { get; set; }
        }
    }

    public sealed partial class PlayServGameServerUplink
    {
        private DataConnection _dataConnection;
        private PlayServUplinkData _data;
        private readonly Dictionary<string, TaskCompletionSource<string>> _dataQueries =
            new Dictionary<string, TaskCompletionSource<string>>(StringComparer.Ordinal);

        /// <summary>Data operations bound to this connection. Reacquire after reconnecting; retained facades cannot send on a new connection.</summary>
        public PlayServUplinkData Data { get { lock (_sync) return _data ??= new PlayServUplinkData(this, _dataConnection); } }
        internal int PendingDataQueryCount { get { lock (_sync) return _dataQueries.Count; } }

        internal sealed class DataConnection
        {
            internal readonly PlayServGameServerContext Context;
            internal readonly IPlayServUplinkSocket Socket;
            internal readonly CancellationTokenSource Stop = new CancellationTokenSource();
            internal readonly CancellationToken Token;
            internal readonly string CatalogueCacheScope = "uplink-data:" + Guid.NewGuid().ToString("N");
            internal PlayServGameServerException Failure;
            internal DataConnection(PlayServGameServerContext context, IPlayServUplinkSocket socket)
            { Context = context; Socket = socket; Token = Stop.Token; }
        }

        private void BeginDataConnection(IPlayServUplinkSocket socket)
        {
            _dataConnection = new DataConnection(_context, socket);
            _data = null;
        }

        private void EndDataConnection(PlayServGameServerException error)
        {
            lock (_sync)
            {
                var connection = _dataConnection;
                _dataConnection = null;
                _data = null;
                foreach (var query in _dataQueries.Values) query.TrySetException(error);
                _dataQueries.Clear();
                if (connection == null) return;
                connection.Failure = error;
                connection.Stop.Cancel();
                connection.Stop.Dispose();
                PlayServRecordsClient.ReleaseCatalogueScope(connection.CatalogueCacheScope);
            }
        }

        internal void ValidateDataConnection(DataConnection connection, PlayServGameServerContext context = null)
        {
            lock (_sync)
            {
                if (_state == PlayServUplinkState.Terminated) throw new PlayServGameServerException(_lastError);
                if (connection == null || !ReferenceEquals(connection, _dataConnection) ||
                    !ReferenceEquals(this, PlayServGameServer.Uplink) || _state != PlayServUplinkState.Connected ||
                    (context != null && !ReferenceEquals(context, connection.Context)) || connection.Context.ShuttingDown)
                    throw UplinkErrors.Exception("uplink_not_connected", true);
                if (_session == null || Seconds() >= _sessionUntil)
                    throw UplinkErrors.Exception("uplink_session_expired", true);
            }
        }

        internal async Task<string> ExecuteDataAsync(DataConnection connection, string id, byte[] bytes,
            Func<CancellationToken, Task> authorize, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TaskCompletionSource<string> completion = null;
            lock (_sync)
            {
                ValidateDataConnection(connection);
                if (id != null)
                {
                    if (_dataQueries.Count >= 128) throw UplinkErrors.Exception("uplink_pending_limit");
                    completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ObserveDataTask(completion.Task);
                    _dataQueries.Add(id, completion);
                }
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, connection.Token);
            deadline.CancelAfter(connection.Context.HttpTimeout);
            try
            {
                var authorization = authorize(deadline.Token);
                ObserveDataTask(authorization);
                await WaitAsync(authorization, deadline.Token).ConfigureAwait(false);
                await SendDataFrameAsync(connection, bytes, deadline.Token).ConfigureAwait(false);
                if (completion == null) return null;
                await WaitAsync(completion.Task, deadline.Token).ConfigureAwait(false);
                return await completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (connection.Token.IsCancellationRequested)
            { throw connection.Failure ?? UplinkErrors.Exception("uplink_connection_lost", true); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                throw new PlayServGameServerException(new PlayServError(PlayServErrorCode.Timeout,
                    "request_timeout", "The uplink data operation timed out.", retryable: true));
            }
            catch (PlayServGameServerException) { throw; }
            catch (PlayServDataException error) { throw PlayServUplinkData.CatalogueFailure(error); }
            catch (ArgumentException) { throw; }
            catch (InvalidOperationException) { throw; }
            catch { throw UplinkErrors.Exception("uplink_send_failed", true); }
            finally { if (id != null) { lock (_sync) _dataQueries.Remove(id); } }
        }

        private async Task SendDataFrameAsync(DataConnection connection, byte[] bytes, CancellationToken ct)
        {
            Task send;
            lock (_sync)
            {
                ct.ThrowIfCancellationRequested();
                ValidateDataConnection(connection);
                try { send = connection.Socket.SendAsync(bytes, ct); }
                catch (OperationCanceledException) { throw; }
                catch { connection.Socket.Abort(); throw UplinkErrors.Exception("uplink_send_failed", true); }
            }
            ObserveDataTask(send);
            try { await WaitAsync(send, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { connection.Socket.Abort(); throw UplinkErrors.Exception("uplink_send_failed", true); }
        }

        internal async Task<PlayServRuntimeDataResponse> SendDataCatalogueAsync(DataConnection connection,
            PlayServRuntimeDataRequest request, CancellationToken ct)
        {
            if (request == null || request.Method != "GET" || request.RelativePath != "data/tables")
                throw new InvalidOperationException("The uplink catalogue transport supports only GET data/tables.");
            var response = await connection.Context.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    ValidateDataConnection(connection);
                    return connection.Context.Transport.SendAsync(new PlayServGameServerHttpRequest
                    {
                        Method = "GET", RelativePath = "data/tables", ServerKey = _session,
                        Timeout = connection.Context.HttpTimeout, Operation = "uplink data catalogue"
                    }, ct);
                }
            }).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            ValidateDataConnection(connection);
            if (response == null) throw PlayServUplinkData.InvalidResult();
            if (response.StatusCode < 200 || response.StatusCode > 299)
                throw new PlayServRuntimeHttpException("The server data catalogue request failed.",
                    response.StatusCode, null, "catalogue_request_failed", false);
            return new PlayServRuntimeDataResponse(response.StatusCode, response.Body, response.ETag,
                response.Location, response.ContentType, response.Headers);
        }

        private void HandleDataReply(string json, IPlayServUplinkSocket socket, bool oversized)
        {
            var body = PlayServUplinkData.ParseReply(json);
            if (body == null || !body.TryGetValue("request_id", out var value) || !(value is string id)) return;
            lock (_sync)
            {
                if (_dataConnection == null || !ReferenceEquals(socket, _dataConnection.Socket) ||
                    !_dataQueries.TryGetValue(id, out var completion)) return;
                if (oversized) completion.TrySetException(UplinkErrors.Exception("frame_too_large"));
                else completion.TrySetResult(json);
            }
        }

        private static void ObserveDataTask(Task task) =>
            _ = task.ContinueWith(failed => { _ = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }
}
