using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Wrapper;

namespace Playserv.Data
{
    internal delegate Task<IPlayServRecordSubscription<T>> PlayServRecordSubscribeDelegate<T>(
        PlayServRecord<T> record,
        string query,
        Dictionary<string, object> variables,
        string rootFieldName,
        CancellationToken ct);

    public sealed class PlayServRecordSet<T>
    {
        private readonly PlayServRecordsClient _client;
        private readonly string _explicitEntityId;
        private readonly Func<
            string,
            Dictionary<string, object>,
            CancellationToken,
            Task<ISharedCollection<T>>> _subscribeAsync;
        private readonly PlayServRecordSubscribeDelegate<T> _subscribeRecordAsync;
        private volatile PlayServDataCapabilities _capabilities = PlayServDataCapabilities.Unknown;
        private PlayServEntityBinding _binding;

        internal PlayServRecordSet(
            PlayServRecordsClient client,
            string explicitEntityId = null,
            Func<string, Dictionary<string, object>, CancellationToken, Task<ISharedCollection<T>>> subscribeAsync = null,
            PlayServRecordSubscribeDelegate<T> subscribeRecordAsync = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _explicitEntityId = explicitEntityId;
            _subscribeAsync = subscribeAsync;
            _subscribeRecordAsync = subscribeRecordAsync;
        }

        /// <summary>
        /// Last resolved table-level ACL snapshot. It is <c>Unknown</c> until entity metadata
        /// has been resolved, and remains advisory because the backend enforces row scope.
        /// </summary>
        public PlayServDataCapabilities Capabilities => _capabilities;

        /// <summary>Resolves and returns the cached runtime table capabilities.</summary>
        public async Task<PlayServDataCapabilities> GetCapabilitiesAsync(
            CancellationToken ct = default)
        {
            var entity = await _client.ResolveEntityMetadataAsync<T>(_explicitEntityId, ct);
            SetBinding(entity);
            return entity.Capabilities;
        }

        /// <summary>Reloads runtime table capabilities from the backend catalogue.</summary>
        public async Task<PlayServDataCapabilities> RefreshCapabilitiesAsync(
            CancellationToken ct = default)
        {
            var entity = await _client.ResolveEntityMetadataAsync<T>(_explicitEntityId, ct, true);
            SetBinding(entity);
            return entity.Capabilities;
        }

        public async Task<PlayServRecord<T>> CreateAsync(
            T value,
            CancellationToken ct = default)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Write, ct);
            return await _client.CreateAsync(this, entity, value, ct);
        }

        public async Task<PlayServRecord<T>> LoadAsync(
            string recordId,
            PlayServLoadOptions options = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("Record ID is required.", nameof(recordId));
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Read, ct);
            return await _client.LoadAsync(this, entity, recordId, options, ct);
        }

        public async Task<PlayServLoadOrCreateResult<T>> LoadOrCreateAsync(
            PlayServNaturalKey<T> naturalKey,
            Func<T> factory,
            CancellationToken ct = default)
        {
            if (naturalKey == null)
                throw new ArgumentNullException(nameof(naturalKey));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Read, ct);
            var existing = await _client.FindByNaturalKeyAsync(this, entity, naturalKey, ct);
            if (existing != null)
                return new PlayServLoadOrCreateResult<T>(existing, false);

            entity = await EnsureAccessAsync(
                entity,
                false,
                PlayServDataAccessOperation.Write,
                ct);
            ct.ThrowIfCancellationRequested();
            var value = factory();
            if (value == null)
                throw new InvalidOperationException("The LoadOrCreate factory returned null.");
            _client.ValidateNaturalKey(value, naturalKey);

            PlayServRecord<T> created;
            try
            {
                created = await _client.CreateAsync(this, entity, value, ct);
            }
            catch (PlayServRecordConflictException conflict)
                when (conflict.Kind == PlayServRecordConflictKind.Unique)
            {
                var conflicted = await _client.FindByNaturalKeyAsync(this, entity, naturalKey, ct);
                if (conflicted != null)
                    return new PlayServLoadOrCreateResult<T>(conflicted, false);
                throw;
            }

            try
            {
                var verified = await _client.LoadAsync(this, entity, created.Id, null, ct);
                return new PlayServLoadOrCreateResult<T>(verified, true);
            }
            catch (PlayServRecordNotFoundException)
            {
                var competing = await _client.FindByNaturalKeyAsync(this, entity, naturalKey, ct);
                if (competing != null)
                    return new PlayServLoadOrCreateResult<T>(competing, false);

                throw new PlayServDataException(
                    $"Backend returned created record '{created.Id}', but the record was not persisted.",
                    backendCode: "created_record_not_persisted",
                    extensions: new Dictionary<string, object>
                    {
                        ["entity_id"] = entity.Id,
                        ["record_id"] = created.Id,
                        ["field"] = naturalKey.Field
                    });
            }
        }

        public async Task<PlayServRecordPage<T>> QueryAsync(
            PlayServRecordQuery<T> query = null,
            PlayServPagination pagination = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var effectiveQuery = query ?? new PlayServRecordQuery<T>();
            QueryBuilder.ValidateRestCapabilities(effectiveQuery.Snapshot(
                pagination?.Cursor,
                pagination?.Limit));
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Read, ct);
            return await _client.QueryAsync(this, entity, effectiveQuery, pagination, ct);
        }

        /// <summary>
        /// Loads consecutive query pages until all matching records are read or
        /// <paramref name="maxRecords"/> is reached.
        /// </summary>
        public async Task<PlayServLoadAllResult<T>> LoadAllAsync(
            PlayServRecordQuery<T> query = null,
            int maxRecords = 1_000,
            int pageSize = 200,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ValidateMaxRecords(maxRecords);
            ValidatePageSize(pageSize);
            query = query ?? new PlayServRecordQuery<T>();
            QueryBuilder.ValidateRestCapabilities(query.Snapshot());

            var records = new List<PlayServRecord<T>>(Math.Min(maxRecords, pageSize));
            var cursor = query.Cursor;
            var observedCursors = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(cursor))
                observedCursors.Add(cursor);

            while (records.Count < maxRecords)
            {
                var limit = Math.Min(pageSize, maxRecords - records.Count);
                var page = await QueryAsync(
                    query,
                    new PlayServPagination(cursor, limit),
                    ct);
                records.AddRange(page.Records);

                if (!page.HasMore || string.IsNullOrWhiteSpace(page.NextCursor))
                    return new PlayServLoadAllResult<T>(records, false, null);

                if (records.Count >= maxRecords)
                    return new PlayServLoadAllResult<T>(records, true, page.NextCursor);

                if (!observedCursors.Add(page.NextCursor))
                {
                    throw new PlayServDataException(
                        "Runtime data pagination returned the same cursor more than once.",
                        backendCode: "pagination_cursor_stalled");
                }

                cursor = page.NextCursor;
            }

            return new PlayServLoadAllResult<T>(records, false, null);
        }

        /// <summary>Loads record IDs concurrently while preserving input order.</summary>
        public Task<PlayServBulkResult<PlayServRecord<T>>> LoadManyAsync(
            IEnumerable<string> recordIds,
            PlayServLoadOptions options = null,
            int maxConcurrency = 4,
            CancellationToken ct = default)
        {
            var ids = ValidateRecordIds(recordIds, nameof(recordIds));
            return ExecuteBulkAsync(
                ids,
                id => id,
                (id, token) => LoadAsync(id, options, token),
                maxConcurrency,
                ct);
        }

        /// <summary>
        /// Loads a relation target by its server-minted record ID. Use the target entity's
        /// record set when the relation points to another schema type.
        /// </summary>
        public Task<PlayServRecord<T>> PopulateAsync(
            string relationRecordId,
            PlayServLoadOptions options = null,
            CancellationToken ct = default) =>
            LoadAsync(relationRecordId, options, ct);

        /// <summary>Populates relation target IDs with bounded point-GET fan-out.</summary>
        public Task<PlayServBulkResult<PlayServRecord<T>>> PopulateManyAsync(
            IEnumerable<string> relationRecordIds,
            PlayServLoadOptions options = null,
            int maxConcurrency = 4,
            CancellationToken ct = default) =>
            LoadManyAsync(relationRecordIds, options, maxConcurrency, ct);

        /// <summary>Deletes one record without first creating a local handle.</summary>
        public async Task DeleteByIdAsync(
            string recordId,
            string etag = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("Record ID is required.", nameof(recordId));
            var entity = await ResolveForAccessAsync(
                false,
                PlayServDataAccessOperation.Write,
                ct);
            await _client.DeleteByIdAsync(entity.Id, recordId.Trim(), etag, ct);
        }

        /// <summary>Saves record handles concurrently. Successful handles retain rotated ETags.</summary>
        public Task<PlayServBulkResult<PlayServRecord<T>>> BulkSaveAsync(
            IEnumerable<PlayServRecord<T>> records,
            int maxConcurrency = 4,
            CancellationToken ct = default)
        {
            var items = ValidateRecords(records, nameof(records));
            return ExecuteBulkAsync(
                items,
                record => record.Id,
                async (record, token) =>
                {
                    await record.SaveAsync(token);
                    return record;
                },
                maxConcurrency,
                ct);
        }

        /// <summary>Deletes record handles concurrently and marks successful handles deleted.</summary>
        public Task<PlayServBulkResult<PlayServRecord<T>>> BulkDeleteAsync(
            IEnumerable<PlayServRecord<T>> records,
            int maxConcurrency = 4,
            CancellationToken ct = default)
        {
            var items = ValidateRecords(records, nameof(records));
            return ExecuteBulkAsync(
                items,
                record => record.Id,
                async (record, token) =>
                {
                    await record.DeleteAsync(token);
                    return record;
                },
                maxConcurrency,
                ct);
        }

        /// <summary>
        /// Loads the complete matching set before starting a non-atomic delete fan-out.
        /// An unrestricted query requires <see cref="PlayServDeleteAllConfirmation.AllRecords"/>.
        /// </summary>
        public async Task<PlayServBulkResult<PlayServRecord<T>>> DeleteAllAsync(
            PlayServRecordQuery<T> query,
            PlayServDeleteAllConfirmation confirmation,
            int maxRecords = 10_000,
            int maxConcurrency = 4,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(typeof(PlayServDeleteAllConfirmation), confirmation))
                throw new ArgumentOutOfRangeException(nameof(confirmation));
            ValidateMaxRecords(maxRecords);
            ValidateConcurrency(maxConcurrency);
            query = query ?? new PlayServRecordQuery<T>();
            QueryBuilder.ValidateRestCapabilities(query.Snapshot());
            if (!string.IsNullOrWhiteSpace(query.Cursor))
            {
                throw new ArgumentException(
                    "DeleteAllAsync does not accept a starting cursor because it must enumerate the complete matching set.",
                    nameof(query));
            }

            var isRestricted = query.Filters.Count > 0 ||
                               !string.IsNullOrWhiteSpace(query.SearchText);
            var expected = isRestricted
                ? PlayServDeleteAllConfirmation.MatchingRecords
                : PlayServDeleteAllConfirmation.AllRecords;
            if (confirmation != expected)
            {
                throw new InvalidOperationException(
                    isRestricted
                        ? "A filtered DeleteAllAsync call requires MatchingRecords confirmation."
                        : "Deleting every record requires explicit AllRecords confirmation.");
            }

            var loaded = await LoadAllAsync(query, maxRecords, 200, ct);
            if (loaded.IsTruncated)
            {
                throw new PlayServDataException(
                    $"DeleteAllAsync matched more than the configured maximum of {maxRecords} records; no records were deleted.",
                    backendCode: "bulk_limit_exceeded",
                    extensions: new Dictionary<string, object>
                    {
                        ["max_records"] = maxRecords,
                        ["next_cursor"] = loaded.NextCursor
                    });
            }

            return await BulkDeleteAsync(loaded.Records, maxConcurrency, ct);
        }

        /// <summary>
        /// Opens a transport-backed live collection using the realtime-compatible subset of
        /// <paramref name="query"/>. Unsupported features fail before transport I/O.
        /// </summary>
        public async Task<ISharedCollection<T>> SubscribeAsync(
            PlayServRecordQuery<T> query = null,
            CancellationToken ct = default)
        {
            if (_subscribeAsync == null)
            {
                throw new InvalidOperationException(
                    "Typed record subscriptions are available from PlayServData.Records<T>().");
            }

            query = query ?? new PlayServRecordQuery<T>();
            var snapshot = query.Snapshot();
            QueryBuilder.ValidateRealtimeCapabilities(snapshot);
            ct.ThrowIfCancellationRequested();

            var entity = await ResolveForAccessAsync(
                false,
                PlayServDataAccessOperation.Read,
                ct);
            var payload = QueryBuilder.BuildCollectionQuery<T>(entity.Name, snapshot, _client.Json);
            return await _subscribeAsync(payload.Query, payload.Variables, ct);
        }

        internal async Task<IPlayServRecordSubscription<T>> SubscribeAsync(
            PlayServRecord<T> record,
            CancellationToken ct)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (_subscribeRecordAsync == null)
            {
                throw new InvalidOperationException(
                    "Typed record subscriptions are available from PlayServData.Records<T>().");
            }
            if (record.IsDeleted)
                throw new InvalidOperationException($"Record '{record.Id}' has been deleted.");

            if (record.IsPartial)
                await record.ReloadAsync(ct);
            if (record.HasPendingChanges)
            {
                throw new InvalidOperationException(
                    "A record with unsaved local changes cannot begin realtime synchronization. " +
                    "Call SaveAsync or ReloadAsync first.");
            }

            var entity = await ResolveForAccessAsync(
                false,
                PlayServDataAccessOperation.Read,
                ct);
            if (!string.Equals(entity.Id, record.EntityId, StringComparison.Ordinal))
                throw new InvalidOperationException("The record belongs to a different entity set.");

            var payload = QueryBuilder.BuildKeyedAllFieldsQuery(entity.Name, record.Id);
            return await _subscribeRecordAsync(
                record,
                payload.Query,
                payload.Variables,
                entity.Name,
                ct);
        }

        public async Task<PlayServSingleton<T>> GetSingletonAsync(
            PlayServLoadOptions options = null,
            CancellationToken ct = default)
        {
            var entity = await ResolveForAccessAsync(true, PlayServDataAccessOperation.Read, ct);
            return await _client.GetSingletonAsync(this, entity, options, ct);
        }

        internal async Task EnsureAccessAsync(
            PlayServDataAccessOperation operation,
            bool singleton,
            CancellationToken ct)
        {
            var entity = _binding;
            if (entity == null || entity.Singleton != singleton)
                entity = await _client.ResolveEntityAsync<T>(_explicitEntityId, singleton, ct);
            await EnsureAccessAsync(entity, singleton, operation, ct);
        }

        private async Task<PlayServEntityBinding> ResolveForAccessAsync(
            bool singleton,
            PlayServDataAccessOperation operation,
            CancellationToken ct)
        {
            var entity = await _client.ResolveEntityAsync<T>(_explicitEntityId, singleton, ct);
            return await EnsureAccessAsync(entity, singleton, operation, ct);
        }

        private async Task<PlayServEntityBinding> EnsureAccessAsync(
            PlayServEntityBinding entity,
            bool singleton,
            PlayServDataAccessOperation operation,
            CancellationToken ct)
        {
            SetBinding(entity);
            var subjectCapabilities = _client.SelectCapabilities(entity.Capabilities);
            var state = operation == PlayServDataAccessOperation.Read
                ? subjectCapabilities.Read
                : subjectCapabilities.Write;
            if (state != PlayServDataCapabilityState.Denied)
                return entity;

            var refreshed = await _client.ResolveEntityAsync<T>(entity.Id, singleton, ct, true);
            SetBinding(refreshed);
            subjectCapabilities = _client.SelectCapabilities(refreshed.Capabilities);
            state = operation == PlayServDataAccessOperation.Read
                ? subjectCapabilities.Read
                : subjectCapabilities.Write;
            if (state != PlayServDataCapabilityState.Denied)
                return refreshed;

            var operationName = operation == PlayServDataAccessOperation.Read ? "read" : "write";
            throw new PlayServDataAccessDeniedException(
                $"Runtime data {operationName} access is disabled for {_client.AccessSubjectLabel} on entity '{refreshed.Id}'.",
                refreshed.Id,
                operation,
                true);
        }

        private void SetBinding(PlayServEntityBinding entity)
        {
            _binding = entity;
            _capabilities = entity?.Capabilities ?? PlayServDataCapabilities.Unknown;
        }

        private static async Task<PlayServBulkResult<TResult>> ExecuteBulkAsync<TInput, TResult>(
            IReadOnlyList<TInput> inputs,
            Func<TInput, string> keySelector,
            Func<TInput, CancellationToken, Task<TResult>> operation,
            int maxConcurrency,
            CancellationToken ct)
        {
            ValidateConcurrency(maxConcurrency);
            ct.ThrowIfCancellationRequested();
            if (inputs.Count == 0)
                return new PlayServBulkResult<TResult>(Array.Empty<PlayServBulkItemResult<TResult>>());

            var results = new PlayServBulkItemResult<TResult>[inputs.Count];
            using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = Enumerable.Range(0, inputs.Count).Select(async index =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var input = inputs[index];
                    var key = keySelector(input);
                    try
                    {
                        var value = await operation(input, ct);
                        results[index] = new PlayServBulkItemResult<TResult>(
                            key,
                            value,
                            PlayServError.None,
                            null);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        results[index] = new PlayServBulkItemResult<TResult>(
                            key,
                            default,
                            ToBulkError(exception),
                            exception);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);
            return new PlayServBulkResult<TResult>(results);
        }

        private static PlayServError ToBulkError(Exception exception)
        {
            if (exception is PlayServDataException dataException)
                return dataException.UnifiedError;
            if (exception is ArgumentException || exception is InvalidOperationException)
            {
                return new PlayServError(
                    PlayServErrorCode.Validation,
                    "bulk_item_invalid",
                    exception.Message);
            }

            return new PlayServError(
                PlayServErrorCode.Unknown,
                "bulk_item_failed",
                "A client-side Records bulk item failed.",
                rawDetails: exception.Message);
        }

        private static IReadOnlyList<string> ValidateRecordIds(
            IEnumerable<string> recordIds,
            string parameterName)
        {
            if (recordIds == null)
                throw new ArgumentNullException(parameterName);
            var result = recordIds.Select((value, index) =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException($"Record ID at index {index} is empty.", parameterName);
                return value.Trim();
            }).ToArray();
            return result;
        }

        private static IReadOnlyList<PlayServRecord<T>> ValidateRecords(
            IEnumerable<PlayServRecord<T>> records,
            string parameterName)
        {
            if (records == null)
                throw new ArgumentNullException(parameterName);
            var result = records.ToArray();
            if (result.Any(record => record == null))
                throw new ArgumentException("Bulk record collections cannot contain null handles.", parameterName);
            return result;
        }

        private static void ValidateConcurrency(int maxConcurrency)
        {
            if (maxConcurrency < 1 || maxConcurrency > 32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxConcurrency),
                    "Bulk concurrency must be between 1 and 32.");
            }
        }

        private static void ValidateMaxRecords(int maxRecords)
        {
            if (maxRecords < 1 || maxRecords > 100_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRecords),
                    "Maximum record count must be between 1 and 100000.");
            }
        }

        private static void ValidatePageSize(int pageSize)
        {
            if (pageSize < 1 || pageSize > 200)
                throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 200.");
        }
    }
}
