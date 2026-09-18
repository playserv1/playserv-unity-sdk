using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Data
{
    internal sealed partial class PlayServRecordsClient
    {
        private const int QueryHydrationConcurrency = 4;
        private static readonly object CatalogueCacheGate = new object();
        private static readonly Dictionary<string, PlayServTableCatalogue> CatalogueCache =
            new Dictionary<string, PlayServTableCatalogue>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SemaphoreSlim> CatalogueLocks =
            new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private readonly PlayServSettings _settings;
        private readonly IPlayServRuntimeHttpClient _http;
        private readonly IJsonCodec _json;
        private readonly bool _requiresClientToken;
        private readonly PlayServDataAccessSubject _accessSubject;
        private readonly string _catalogueCacheScope;

        internal PlayServRecordsClient(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient http,
            IJsonCodec json,
            bool requiresClientToken = true,
            PlayServDataAccessSubject accessSubject = PlayServDataAccessSubject.Client,
            string catalogueCacheScope = null,
            Func<bool> isReferenceContextCurrent = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _requiresClientToken = requiresClientToken;
            CaptureReferenceContext(isReferenceContextCurrent);
            _accessSubject = accessSubject;
            _catalogueCacheScope = string.IsNullOrWhiteSpace(catalogueCacheScope)
                ? string.Concat(
                    _settings.BackendServerAddress ?? string.Empty,
                    "\n",
                    _settings.ClientToken ?? string.Empty,
                    "\n",
                    _accessSubject.ToString())
                : catalogueCacheScope.Trim();
        }

        internal static PlayServRecordsClient CreateDefault()
        {
            var settings = PlayServ.Settings;
            var json = new NewtonsoftJsonCodec();
            var http = PlayServRuntimeHttpClientResolver.Create(
                new PlayServHttpModuleContext(settings.ToRuntimeSettings(), json));
            return new PlayServRecordsClient(settings, http, json,
                isReferenceContextCurrent: () => ReferenceEquals(PlayServ.Settings, settings));
        }

        internal IJsonCodec Json => _json;

        // Only callers owning an isolated transient scope may release it. Do not dispose the
        // semaphore: an already-cancelled/in-flight reader may still need to release its lease.
        internal static void ReleaseCatalogueScope(string scope)
        {
            lock (CatalogueCacheGate)
            {
                CatalogueCache.Remove(scope);
                CatalogueLocks.Remove(scope);
            }
        }

        internal PlayServDataSubjectCapabilities SelectCapabilities(
            PlayServDataCapabilities capabilities)
        {
            capabilities = capabilities ?? PlayServDataCapabilities.Unknown;
            switch (_accessSubject)
            {
                case PlayServDataAccessSubject.Server:
                    return capabilities.Server;
                case PlayServDataAccessSubject.Backend:
                    return capabilities.Backend;
                default:
                    return capabilities.Client;
            }
        }

        internal string AccessSubjectLabel => _accessSubject == PlayServDataAccessSubject.Server
            ? "Unity dedicated servers"
            : _accessSubject == PlayServDataAccessSubject.Backend
                ? "backend services"
                : "Unity clients";

        internal async Task<IReadOnlyList<PlayServDataTableInfo>> GetTablesAsync(
            bool forceRefresh,
            CancellationToken ct)
        {
            var catalogue = await GetCatalogueAsync(forceRefresh, ct);
            return catalogue.Entities.Select(entity => entity.Info).ToArray();
        }

        internal async Task<PlayServDataTableInfo> GetTableAsync(
            string idOrName,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
                throw new ArgumentException("Table ID or name is required.", nameof(idOrName));
            var normalized = idOrName.Trim();
            var catalogue = await GetCatalogueAsync(false, ct);
            if (catalogue.TryResolve(normalized, out var binding))
                return binding.Info;
            throw new PlayServDataTableNotFoundException(normalized);
        }

        internal async Task<PlayServEntityBinding> ResolveEntityAsync<T>(
            string explicitEntityId,
            bool expectedSingleton,
            CancellationToken ct,
            bool forceRefresh = false)
        {
            var resolved = await ResolveEntityMetadataAsync<T>(explicitEntityId, ct, forceRefresh);
            if (resolved.Singleton != expectedSingleton)
            {
                var expected = expectedSingleton ? "singleton" : "table";
                throw new InvalidOperationException($"Entity '{resolved.Name}' is not a {expected}.");
            }
            return resolved;
        }

        internal async Task<PlayServEntityBinding> ResolveEntityMetadataAsync<T>(
            string explicitEntityId,
            CancellationToken ct,
            bool forceRefresh = false)
        {
            var catalogue = await GetCatalogueAsync(forceRefresh, ct);
            if (!string.IsNullOrWhiteSpace(explicitEntityId))
            {
                var normalizedId = explicitEntityId.Trim();
                if (catalogue.ById.TryGetValue(normalizedId, out var explicitBinding))
                    return explicitBinding;
                throw new PlayServDataException(
                    $"No runtime data entity with ID '{normalizedId}' is visible.",
                    404,
                    "entity_not_found");
            }

            var typeName = typeof(T).Name;
            var candidates = MatchTypeName(catalogue, typeName);
            if (candidates.Count == 0)
            {
                throw new PlayServDataException(
                    $"No runtime data entity named '{typeName}' is visible. Pass an explicit ent_* ID when the schema name differs from the CLR type name.",
                    404,
                    "entity_not_found");
            }
            if (candidates.Count > 1)
                throw new InvalidOperationException($"More than one runtime entity matches CLR type '{typeName}'. Use the explicit entity ID overload.");
            return candidates[0];
        }

        private static List<PlayServEntityBinding> MatchTypeName(PlayServTableCatalogue catalogue, string typeName)
        {
            var candidates = new List<PlayServEntityBinding>();
            foreach (var binding in catalogue.Entities)
            {
                if (string.Equals(binding.Name, typeName, StringComparison.Ordinal))
                {
                    candidates.Clear();
                    candidates.Add(binding);
                    break;
                }
                if (string.Equals(binding.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(binding);
            }

            return candidates;
        }

        private async Task<PlayServTableCatalogue> GetCatalogueAsync(
            bool forceRefresh,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var cacheKey = _catalogueCacheScope;
            SemaphoreSlim catalogueLock;
            PlayServTableCatalogue observedCatalogue = null;
            lock (CatalogueCacheGate)
            {
                if (!forceRefresh && CatalogueCache.TryGetValue(cacheKey, out var cached))
                    return cached;
                CatalogueCache.TryGetValue(cacheKey, out observedCatalogue);
                if (!CatalogueLocks.TryGetValue(cacheKey, out catalogueLock))
                {
                    catalogueLock = new SemaphoreSlim(1, 1);
                    CatalogueLocks[cacheKey] = catalogueLock;
                }
            }

            await catalogueLock.WaitAsync(ct);
            try
            {
                lock (CatalogueCacheGate)
                {
                    if (CatalogueCache.TryGetValue(cacheKey, out var cached) &&
                        (!forceRefresh || !ReferenceEquals(cached, observedCatalogue)))
                    {
                        return cached;
                    }
                }

                var response = await SendAsync("GET", "data/tables", null, null, null, ct);
                var root = ParseObject(response.Body, "Table catalogue");
                var entities = new List<PlayServEntityBinding>();
                foreach (var item in GetEnumerable(root, "data"))
                {
                    var table = AsDictionary(item);
                    var entityId = GetString(table, "entity_id");
                    var name = GetString(table, "name");
                    if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(name))
                        continue;
                    entities.Add(new PlayServEntityBinding(
                        entityId,
                        name,
                        GetString(table, "description"),
                        GetBool(table, "singleton"),
                        GetNullableInt64(table, "row_count") ?? 0,
                        GetDate(table, "updated_at"),
                        ParseCapabilities(table)));
                }

                var catalogue = new PlayServTableCatalogue(entities);
                lock (CatalogueCacheGate)
                    CatalogueCache[cacheKey] = catalogue;
                return catalogue;
            }
            finally
            {
                catalogueLock.Release();
            }
        }

        internal static void ValidateCreateIdempotencyKey(string idempotencyKey)
        {
            ValidateWriteHeader(idempotencyKey, nameof(idempotencyKey));
            if (idempotencyKey != null && idempotencyKey.Length > 128)
                throw new ArgumentException("Idempotency key must not exceed 128 characters.", nameof(idempotencyKey));
        }

        internal async Task<PlayServRecord<T>> CreateAsync<T>(
            PlayServRecordSet<T> owner,
            PlayServEntityBinding entity,
            T value,
            CancellationToken ct,
            string idempotencyKey = null)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var body = ToRecordFields(value);
            var response = await SendAsync(
                "POST",
                $"data/tables/{Escape(entity.Id)}/records",
                _json.Serialize(body),
                null,
                idempotencyKey ?? Guid.NewGuid().ToString("D"),
                ct,
                entity.Id);
            return ParseRecord(owner, entity.Id, response.Body, response.ETag, false);
        }

        internal async Task<PlayServBulkCreateResult> BulkCreateAsync<T>(
            PlayServEntityBinding entity,
            IReadOnlyList<T> values,
            IReadOnlyDictionary<string, object> defaults,
            CancellationToken ct,
            string idempotencyKey = null)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Count < 1 || values.Count > 200)
                throw new ArgumentOutOfRangeException(nameof(values), "Atomic bulk create requires between 1 and 200 records.");

            var rows = new List<Dictionary<string, object>>(values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] == null)
                    throw new ArgumentException($"Record at index {index} is null.", nameof(values));
                rows.Add(ToRecordFields(values[index]));
            }

            Dictionary<string, object> defaultFields = null;
            if (defaults != null)
            {
                defaultFields = defaults.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                RemoveSystemFields(defaultFields);
            }

            var body = new Dictionary<string, object>
            {
                ["defaults"] = defaultFields,
                ["records"] = rows
            };
            var response = await SendAsync(
                "POST",
                $"data/tables/{Escape(entity.Id)}/records:bulk-create",
                _json.Serialize(body),
                null,
                idempotencyKey ?? Guid.NewGuid().ToString("D"),
                ct,
                entity.Id);
            var root = ParseObject(response.Body, "Bulk create");
            var ids = GetEnumerable(root, "ids")
                .Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            var created = checked((int)(GetNullableInt64(root, "created") ?? -1));
            if (created != values.Count || ids.Length != values.Count)
            {
                throw new PlayServDataException(
                    "Bulk create response did not contain one server-minted ID for every submitted record.",
                    backendCode: "invalid_response");
            }
            return new PlayServBulkCreateResult(ids, created);
        }

        internal async Task<PlayServRecord<T>> LoadAsync<T>(
            PlayServRecordSet<T> owner,
            PlayServEntityBinding entity,
            string recordId,
            PlayServLoadOptions options,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("Record ID is required.", nameof(recordId));

            var path = $"data/tables/{Escape(entity.Id)}/records/{Escape(recordId.Trim())}" +
                       BuildLoadQuery(options);
            var response = await SendAsync(
                "GET",
                path,
                null,
                null,
                null,
                ct,
                entity.Id);
            return ParseRecord(owner, entity.Id, response.Body, response.ETag, options?.IsPartial == true);
        }

        internal async Task<PlayServRecord<T>> FindByNaturalKeyAsync<T>(
            PlayServRecordSet<T> owner,
            PlayServEntityBinding entity,
            PlayServNaturalKey<T> key,
            CancellationToken ct,
            PlayServLoadOptions options = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var value = Convert.ToString(key.Value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Natural-key value must have a non-empty text representation.", nameof(key));
            var path = $"data/tables/{Escape(entity.Id)}/records:by-natural-key" +
                       $"?field={Escape(key.Field)}&value={Escape(value)}" +
                       BuildLoadQuery(options, "&");
            try
            {
                var response = await SendAsync("GET", path, null, null, null, ct, entity.Id);
                return ParseRecord(owner, entity.Id, response.Body, response.ETag, options?.IsPartial == true);
            }
            catch (PlayServRecordNotFoundException)
            {
                return null;
            }
        }

        internal async Task<PlayServDeleteByFilterResult> DeleteByFilterAsync<T>(
            PlayServEntityBinding entity,
            PlayServRecordQuery<T> query,
            bool deleteAll,
            CancellationToken ct)
        {
            var snapshot = (query ?? new PlayServRecordQuery<T>()).Snapshot();
            var body = new Dictionary<string, object>
            {
                ["q"] = snapshot.SearchText,
                ["filters"] = snapshot.Filters.Select(FilterToWire).ToList(),
                ["all"] = deleteAll ? (bool?)true : null
            };
            var response = await SendAsync(
                "POST",
                $"data/tables/{Escape(entity.Id)}/records:delete-by-filter",
                _json.Serialize(body),
                null,
                null,
                ct,
                entity.Id);
            var root = ParseObject(response.Body, "Delete by filter");
            var ids = GetEnumerable(root, "ids")
                .Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            var deleted = checked((int)(GetNullableInt64(root, "deleted") ?? -1));
            if (deleted < 0 || deleted != ids.Length)
            {
                throw new PlayServDataException(
                    "Delete-by-filter response contained inconsistent deleted IDs.",
                    backendCode: "invalid_response");
            }
            return new PlayServDeleteByFilterResult(ids, deleted);
        }

        internal async Task<PlayServRecordPage<T>> QueryViewAsync<T>(
            PlayServRecordSet<T> owner,
            PlayServEntityBinding entity,
            string viewId,
            PlayServPagination pagination,
            CancellationToken ct)
        {
            var path = $"data/tables/{Escape(entity.Id)}/records?view_id={Escape(viewId)}" +
                "&limit=" + pagination.Limit.ToString(CultureInfo.InvariantCulture);
            if (pagination.Cursor != null)
                path += "&cursor=" + Escape(pagination.Cursor);
            var response = await SendAsync("GET", path, null, null, null, ct, entity.Id);
            var root = ParseObject(response.Body, "Record View query");
            RequireQueryPageEnvelope(root);
            return ParseRecordPage(root, ParseQueryRecords(owner, entity, root, true));
        }

        internal async Task<PlayServRecordPage<T>> QueryAsync<T>(
            PlayServRecordSet<T> owner,
            PlayServEntityBinding entity,
            PlayServRecordQuery<T> query,
            PlayServPagination pagination,
            CancellationToken ct,
            bool requireBatchEnvelope = false)
        {
            query = query ?? new PlayServRecordQuery<T>();
            var cursor = pagination?.Cursor ?? query.Cursor;
            var limit = pagination?.Limit ?? query.Limit;
            var snapshot = query.Snapshot(cursor, limit);
            var body = new Dictionary<string, object>
            {
                ["q"] = snapshot.SearchText,
                ["filters"] = snapshot.Filters.Select(FilterToWire).ToList(),
                ["sort"] = snapshot.Sort.Select(sort => new Dictionary<string, object>
                {
                    ["field"] = sort.Field,
                    ["dir"] = sort.Descending ? "desc" : "asc"
                }).ToList(),
                ["hidden_columns"] = snapshot.HiddenColumns.Count == 0 ? null : snapshot.HiddenColumns,
                ["cursor"] = snapshot.Cursor,
                ["limit"] = snapshot.Limit
            };
            var response = await SendAsync(
                "POST",
                $"data/tables/{Escape(entity.Id)}/records:query",
                _json.Serialize(body),
                null,
                null,
                ct,
                entity.Id);
            var root = ParseObject(response.Body, "Record query");
            if (requireBatchEnvelope) RequireQueryPageEnvelope(root);
            var records = ParseQueryRecords(owner, entity, root, snapshot.ProducesPartialRecords);
            if (snapshot.RequiresHydration && records.Count > 0)
                records = (await HydrateQueryRecordsAsync(owner, entity, records, snapshot, ct)).ToList();
            return ParseRecordPage(root, records);
        }

        private static void RequireQueryPageEnvelope(IDictionary<string, object> root)
        {
            if (!TryGet(root, "data", out var data) || !(data is IList) ||
                !TryGet(root, "page", out var page) || !(page is IDictionary<string, object> pageFields) ||
                !TryGet(pageFields, "has_more", out var hasMore) || !(hasMore is bool))
                throw new PlayServDataException("Record query response was incomplete.", backendCode: "invalid_response");
        }

        private List<PlayServRecord<T>> ParseQueryRecords<T>(PlayServRecordSet<T> owner,
            PlayServEntityBinding entity, Dictionary<string, object> root, bool isPartial) =>
            GetEnumerable(root, "data")
                .Select(item => ParseRecord(owner, entity.Id, _json.Serialize(item), null, isPartial)).ToList();

        private static PlayServRecordPage<T> ParseRecordPage<T>(IDictionary<string, object> root,
            IReadOnlyList<PlayServRecord<T>> records)
        {
            var page = TryGet(root, "page", out var pageValue)
                ? AsDictionary(pageValue)
                : new Dictionary<string, object>();
            return new PlayServRecordPage<T>(
                records,
                GetString(page, "cursor_next"),
                GetString(page, "cursor_prev"),
                GetBool(page, "has_more"),
                GetNullableInt64(root, "total_estimate"));
        }

        private async Task<IReadOnlyList<PlayServRecord<T>>> HydrateQueryRecordsAsync<T>(
            PlayServRecordSet<T> owner,
            PlayServEntityBinding entity,
            IReadOnlyList<PlayServRecord<T>> records,
            PlayServQuerySnapshot snapshot,
            CancellationToken ct)
        {
            var options = new PlayServLoadOptions
            {
                Fields = snapshot.SelectedFields.Count == 0 ? null : snapshot.SelectedFields,
                Expand = snapshot.Includes.Count == 0
                    ? null
                    : snapshot.Includes.Select(include => include.WirePath).ToArray()
            };
            using var gate = new SemaphoreSlim(QueryHydrationConcurrency, QueryHydrationConcurrency);
            var tasks = records.Select(async record =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await LoadAsync(owner, entity, record.Id, options, ct);
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            return await Task.WhenAll(tasks);
        }

        internal async Task<PlayServSingleton<T>> GetSingletonAsync<T>(
            PlayServRecordSet<T> owner,
            PlayServEntityBinding entity,
            PlayServLoadOptions options,
            CancellationToken ct)
        {
            var response = await SendAsync(
                "GET",
                $"data/tables/{Escape(entity.Id)}/singleton",
                null,
                null,
                null,
                ct,
                entity.Id);
            return ParseSingleton(owner, entity.Id, response.Body, response.ETag, options?.IsPartial == true);
        }

        internal async Task<PlayServRecord<T>> SaveAsync<T>(
            PlayServRecord<T> record,
            IReadOnlyDictionary<string, object> patch,
            CancellationToken ct)
        {
            var response = await SendAsync(
                "PATCH",
                $"data/tables/{Escape(record.EntityId)}/records/{Escape(record.Id)}",
                _json.Serialize(patch),
                record.ETag,
                null,
                ct,
                record.EntityId);
            return ParseRecord(record.OwnerSet, record.EntityId, response.Body, response.ETag, false);
        }

        internal async Task DeleteAsync<T>(PlayServRecord<T> record, CancellationToken ct)
        {
            await DeleteByIdAsync(record.EntityId, record.Id, record.ETag, ct);
        }

        internal async Task DeleteByIdAsync(
            string entityId,
            string recordId,
            string etag,
            CancellationToken ct)
        {
            await SendAsync(
                "DELETE",
                $"data/tables/{Escape(entityId)}/records/{Escape(recordId)}",
                null,
                string.IsNullOrWhiteSpace(etag) ? null : etag.Trim(),
                null,
                ct,
                entityId);
        }

        internal async Task<PlayServSingleton<T>> SaveSingletonAsync<T>(
            PlayServSingleton<T> singleton,
            IReadOnlyDictionary<string, object> patch,
            CancellationToken ct)
        {
            var response = await SendAsync(
                "PATCH",
                $"data/tables/{Escape(singleton.EntityId)}/singleton",
                _json.Serialize(patch),
                singleton.ETag,
                null,
                ct,
                singleton.EntityId);
            return ParseSingleton(singleton.OwnerSet, singleton.EntityId, response.Body, response.ETag, false);
        }

        internal Dictionary<string, object> ToRecordFields<T>(T value)
        {
            var plain = AsDictionary(_json.ParseToPlainValue(_json.Serialize(value, ReferenceJsonOptions())));
            RemoveSystemFields(plain);
            return plain;
        }

        internal void ValidateNaturalKey<T>(T value, PlayServNaturalKey<T> key)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var fields = ToRecordFields(value);
            if (!fields.TryGetValue(key.Field, out var actual) || actual == null)
            {
                throw new InvalidOperationException(
                    $"The LoadOrCreate factory value must contain natural-key field '{key.Field}'.");
            }

            var actualJson = _json.Serialize(_json.ToPlainValue(actual));
            var expectedJson = _json.Serialize(_json.ToPlainValue(key.Value));
            if (!string.Equals(actualJson, expectedJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The LoadOrCreate factory value for '{key.Field}' does not match the requested natural key.");
            }
        }

        internal Dictionary<string, object> ParseSnapshot(string canonicalJson) =>
            AsDictionary(_json.ParseToPlainValue(canonicalJson));

        internal string Canonical(object value) => _json.ToCanonicalJson(CanonicalValue(value));

        internal T PreserveChangesDuringSave<T>(
            IReadOnlyDictionary<string, object> sent,
            T currentValue,
            T serverValue,
            string serverSnapshot)
        {
            // Do not resurrect a Value explicitly cleared during I/O. A subsequent Save
            // still rejects null through its existing validation.
            if (currentValue == null)
                return currentValue;

            var current = ToRecordFields(currentValue);
            Dictionary<string, object> merged = null;
            foreach (var key in sent.Keys.Union(current.Keys, StringComparer.Ordinal))
            {
                var hadBefore = sent.TryGetValue(key, out var before);
                var hasNow = current.TryGetValue(key, out var now);
                if (hadBefore == hasNow &&
                    string.Equals(Canonical(before), Canonical(now), StringComparison.Ordinal))
                    continue;

                if (merged == null)
                    merged = ParseSnapshot(serverSnapshot);
                // Use the same top-level boundary as Save's patch: nested objects and arrays
                // are one field. Removal is distinct from an explicit null local value.
                if (hasNow)
                    merged[key] = now;
                else
                    merged.Remove(key);
            }
            // Server normalization of fields not edited during I/O remains authoritative.
            // The caller keeps serverSnapshot unchanged, so only unacknowledged edits stay dirty.
            return merged == null ? serverValue : ConvertRecordValue<T>(merged);
        }

        private async Task<PlayServRuntimeDataResponse> SendAsync(
            string method,
            string path,
            string jsonBody,
            string ifMatch,
            string idempotencyKey,
            CancellationToken ct,
            string accessEntityId = null)
        {
            ct.ThrowIfCancellationRequested();
            if (_isReferenceClient) ValidateReferenceContext();
            if (_requiresClientToken && string.IsNullOrWhiteSpace(_settings.ClientToken))
                throw new InvalidOperationException("PlayServ.Settings.ClientToken must contain a public pk_* token before using records.");

            string bearer = null;
            if (_requiresClientToken && _settings.RuntimeTokenProvider != null)
                bearer = await _settings.RuntimeTokenProvider.GetTokenAsync(ct);
            else if (_requiresClientToken && !string.IsNullOrWhiteSpace(_settings.PlayerAccessToken))
                bearer = _settings.PlayerAccessToken;

            if (_isReferenceClient) ValidateReferenceContext();
            PlayServRuntimeDataResponse response;
            try
            {
                response = await _http.SendDataAsync(new PlayServRuntimeDataRequest
                {
                    Method = method,
                    RelativePath = path,
                    ClientToken = _settings.ClientToken,
                    BearerToken = bearer,
                    JsonBody = jsonBody,
                    IfMatch = ifMatch,
                    IdempotencyKey = idempotencyKey
                }, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServRuntimeHttpException ex)
            {
                throw MapException(ex, accessEntityId);
            }
            catch (Exception ex)
            {
                throw new PlayServDataException(
                    "Runtime data request failed before a server response was received.",
                    0,
                    "transport_error",
                    true,
                    innerException: ex);
            }
            if (_isReferenceClient) ValidateReferenceContext();
            return response;
        }

        private Exception MapException(
            PlayServRuntimeHttpException ex,
            string accessEntityId)
        {
            var problem = ParseProblem(ex.ResponseBody);
            var backendCode = string.IsNullOrWhiteSpace(ex.BackendCode)
                ? GetString(problem, "code")
                : ex.BackendCode;
            var detail = FirstNonEmpty(
                ex.ProblemDetail,
                GetString(problem, "detail"),
                ex.Message);
            var extensions = problem
                .Where(pair => !IsProblemCoreField(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (ex.StatusCode == 403 &&
                (backendCode == "table_read_forbidden" || backendCode == "table_write_forbidden"))
            {
                var operation = backendCode == "table_read_forbidden"
                    ? PlayServDataAccessOperation.Read
                    : PlayServDataAccessOperation.Write;
                return new PlayServDataAccessDeniedException(
                    detail,
                    accessEntityId,
                    operation,
                    false,
                    extensions,
                    ex);
            }
            if (ex.StatusCode == 404)
                return new PlayServRecordNotFoundException(detail, backendCode, extensions, ex);
            if (ex.StatusCode == 409 || ex.StatusCode == 412)
            {
                var kind = ex.StatusCode == 412 || backendCode == "precondition_failed"
                    ? PlayServRecordConflictKind.StaleVersion
                    : backendCode == "record_unique_conflict"
                        ? PlayServRecordConflictKind.Unique
                        : PlayServRecordConflictKind.Unknown;
                return new PlayServRecordConflictException(
                    detail,
                    ex.StatusCode,
                    backendCode,
                    kind,
                    GetString(problem, "existing_record_id"),
                    GetString(problem, "field"),
                    extensions,
                    ex);
            }

            return new PlayServDataException(
                detail,
                ex.StatusCode,
                backendCode,
                ex.IsNetworkError,
                extensions,
                ex);
        }

        private PlayServRecord<T> ParseRecord<T>(
            PlayServRecordSet<T> owner,
            string entityId,
            string json,
            string etag,
            bool isPartial)
        {
            var root = ParseObject(json, "Record");
            var id = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(id))
                throw new PlayServDataException("Record response did not contain a server-minted ID.", 0, "invalid_response");

            var createdAt = GetDate(root, "created_at");
            var updatedAt = GetDate(root, "updated_at");
            var ownerId = GetString(root, "owner");
            RemoveSystemFields(root);
            var value = ParseReferenceFields<T>(root, out var canonical);
            return new PlayServRecord<T>(
                owner,
                this,
                id,
                entityId,
                value,
                createdAt,
                updatedAt,
                ownerId,
                FirstNonEmpty(etag, ETagFrom(updatedAt)),
                canonical,
                isPartial);
        }

        private PlayServSingleton<T> ParseSingleton<T>(
            PlayServRecordSet<T> owner,
            string entityId,
            string json,
            string etag,
            bool isPartial)
        {
            var root = ParseObject(json, "Singleton");
            var createdAt = GetDate(root, "created_at");
            var updatedAt = GetDate(root, "updated_at");
            RemoveSystemFields(root);
            var value = ParseReferenceFields<T>(root, out var canonical);
            return new PlayServSingleton<T>(
                owner,
                this,
                entityId,
                value,
                createdAt,
                updatedAt,
                FirstNonEmpty(etag, ETagFrom(updatedAt)),
                canonical,
                isPartial);
        }

        private Dictionary<string, object> ParseProblem(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                return ParseObject(json, "Problem Details");
            }
            catch
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }
        }

        private Dictionary<string, object> ParseObject(string json, string label)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new PlayServDataException($"{label} response body was empty.", 0, "invalid_response");
            var value = _json.ParseToPlainValue(json);
            if (!(value is IDictionary))
                throw new PlayServDataException($"{label} response body was not a JSON object.", 0, "invalid_response");
            return AsDictionary(value);
        }

        private static Dictionary<string, object> AsDictionary(object value)
        {
            if (value is IDictionary<string, object> generic)
                return new Dictionary<string, object>(generic, StringComparer.Ordinal);
            if (value is IDictionary dictionary)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry pair in dictionary)
                    result[Convert.ToString(pair.Key, CultureInfo.InvariantCulture)] = pair.Value;
                return result;
            }

            throw new InvalidOperationException("Expected a JSON object.");
        }

        private static object CanonicalValue(object value)
        {
            if (value is IDictionary<string, object> generic)
            {
                var sorted = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var pair in generic)
                    sorted[pair.Key] = CanonicalValue(pair.Value);
                return sorted;
            }
            if (value is IDictionary dictionary)
            {
                var sorted = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry pair in dictionary)
                    sorted[Convert.ToString(pair.Key, CultureInfo.InvariantCulture)] = CanonicalValue(pair.Value);
                return sorted;
            }
            if (value is IEnumerable enumerable && !(value is string) && !(value is byte[]))
                return enumerable.Cast<object>().Select(CanonicalValue).ToList();
            return value;
        }

        private static IEnumerable<object> GetEnumerable(Dictionary<string, object> source, string key)
        {
            if (!TryGet(source, key, out var value) || value == null)
                return Array.Empty<object>();
            if (value is IEnumerable<object> generic)
                return generic;
            if (value is IEnumerable enumerable)
                return enumerable.Cast<object>();
            return Array.Empty<object>();
        }

        private static PlayServDataCapabilities ParseCapabilities(
            IDictionary<string, object> table)
        {
            var readPolicy = GetString(table, "read");
            if (!TryGet(table, "acl", out var aclValue) || !(aclValue is IDictionary))
            {
                return new PlayServDataCapabilities(null, null, null, readPolicy);
            }

            var acl = AsDictionary(aclValue);
            return new PlayServDataCapabilities(
                ParseSubjectCapabilities(acl, "client"),
                ParseSubjectCapabilities(acl, "server"),
                ParseSubjectCapabilities(acl, "backend"),
                readPolicy);
        }

        private static PlayServDataSubjectCapabilities ParseSubjectCapabilities(
            IDictionary<string, object> acl,
            string subject)
        {
            if (!TryGet(acl, subject, out var subjectValue) || !(subjectValue is IDictionary))
            {
                return new PlayServDataSubjectCapabilities(
                    PlayServDataCapabilityState.Unknown,
                    PlayServDataCapabilityState.Unknown);
            }

            var flags = AsDictionary(subjectValue);
            return new PlayServDataSubjectCapabilities(
                GetCapabilityState(flags, "read"),
                GetCapabilityState(flags, "write"));
        }

        private static PlayServDataCapabilityState GetCapabilityState(
            IDictionary<string, object> source,
            string key)
        {
            if (!TryGet(source, key, out var value) || value == null)
                return PlayServDataCapabilityState.Unknown;
            if (value is bool boolean)
                return boolean
                    ? PlayServDataCapabilityState.Allowed
                    : PlayServDataCapabilityState.Denied;
            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
            {
                return parsed
                    ? PlayServDataCapabilityState.Allowed
                    : PlayServDataCapabilityState.Denied;
            }
            return PlayServDataCapabilityState.Unknown;
        }

        private static Dictionary<string, object> FilterToWire(PlayServQueryFilter filter) =>
            new Dictionary<string, object>
            {
                ["field"] = filter.Field,
                ["op"] = PlayServRecordWireNames.OperatorToWire(filter.Operator),
                ["value"] = filter.Value,
                ["value2"] = filter.Value2
            };

        private static string BuildLoadQuery(PlayServLoadOptions options, string prefix = "?")
        {
            if (options == null)
                return string.Empty;

            var query = new List<string>();
            if (options.Fields != null && options.Fields.Count > 0)
                query.Add("fields=" + Escape(string.Join(",", options.Fields)));
            if (options.Expand != null && options.Expand.Count > 0)
                query.Add("expand=" + Escape(string.Join(",", options.Expand)));
            return query.Count == 0 ? string.Empty : prefix + string.Join("&", query);
        }

        private static void RemoveSystemFields(IDictionary<string, object> fields)
        {
            foreach (var key in fields.Keys.Where(IsSystemField).ToList())
                fields.Remove(key);
        }

        private static bool IsSystemField(string key) =>
            string.Equals(key, "id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "created_at", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "updated_at", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "owner", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "owner_player_id", StringComparison.OrdinalIgnoreCase);

        private static bool IsProblemCoreField(string key) =>
            key == "type" || key == "title" || key == "status" || key == "code" ||
            key == "detail" || key == "trace_id" || key == "error";

        private static bool TryGet(IDictionary<string, object> source, string key, out object value)
        {
            if (source.TryGetValue(key, out value))
                return true;
            foreach (var pair in source)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        private static string GetString(IDictionary<string, object> source, string key) =>
            TryGet(source, key, out var value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : string.Empty;

        private static bool GetBool(IDictionary<string, object> source, string key)
        {
            if (!TryGet(source, key, out var value) || value == null)
                return false;
            if (value is bool boolean)
                return boolean;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed) && parsed;
        }

        private static long? GetNullableInt64(IDictionary<string, object> source, string key)
        {
            if (!TryGet(source, key, out var value) || value == null)
                return null;
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static DateTimeOffset GetDate(IDictionary<string, object> source, string key)
        {
            var text = GetString(source, key);
            return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value)
                ? value
                : default;
        }

        private static string ETagFrom(DateTimeOffset updatedAt) =>
            updatedAt == default
                ? string.Empty
                : string.Concat("\"", updatedAt.UtcDateTime.Ticks.ToString("x", CultureInfo.InvariantCulture), "\"");

        private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    internal enum PlayServDataAccessSubject
    {
        Client,
        Server,
        Backend
    }

    internal sealed class PlayServEntityBinding
    {
        public PlayServEntityBinding(
            string id,
            string name,
            string description,
            bool singleton,
            long rowCount,
            DateTimeOffset updatedAt,
            PlayServDataCapabilities capabilities)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Entity ID is required.", nameof(id));

            Id = id;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            Singleton = singleton;
            RowCount = rowCount;
            UpdatedAt = updatedAt;
            Capabilities = capabilities ?? PlayServDataCapabilities.Unknown;
            Info = new PlayServDataTableInfo(
                Id,
                Name,
                Description,
                Singleton,
                RowCount,
                UpdatedAt,
                Capabilities.ReadPolicy,
                Capabilities);
        }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }

        public bool Singleton { get; }

        public long RowCount { get; }

        public DateTimeOffset UpdatedAt { get; }

        public PlayServDataCapabilities Capabilities { get; }

        public PlayServDataTableInfo Info { get; }
    }

    internal sealed class PlayServTableCatalogue
    {
        public PlayServTableCatalogue(IReadOnlyList<PlayServEntityBinding> entities)
        {
            Entities = entities ?? Array.Empty<PlayServEntityBinding>();
            ById = Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        }

        public IReadOnlyList<PlayServEntityBinding> Entities { get; }

        public IReadOnlyDictionary<string, PlayServEntityBinding> ById { get; }

        public bool TryResolve(string idOrName, out PlayServEntityBinding binding)
        {
            if (ById.TryGetValue(idOrName, out binding))
                return true;

            binding = Entities.FirstOrDefault(entity =>
                string.Equals(entity.Name, idOrName, StringComparison.Ordinal));
            if (binding != null)
                return true;

            var candidates = Entities.Where(entity =>
                string.Equals(entity.Name, idOrName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 1)
            {
                binding = candidates[0];
                return true;
            }

            binding = null;
            return false;
        }
    }
}
