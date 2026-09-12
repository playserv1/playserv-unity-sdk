using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Data
{
    /// <summary>Required policy when an upsert addresses an existing record.</summary>
    public enum PlayServUpsertMode
    {
        /// <summary>Keep an existing record unchanged.</summary>
        Seed,
        /// <summary>Update supplied fields and leave other fields unchanged.</summary>
        Managed
    }

    /// <summary>One addressed row in a native atomic bulk upsert. Record is a DTO or JSON object.</summary>
    public sealed class PlayServBulkUpsertRow<T>
    {
        public PlayServBulkUpsertRow(PlayServNaturalKey<T> key, object record)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }
        public PlayServNaturalKey<T> Key { get; }
        public object Record { get; }
    }

    /// <summary>Server acknowledgment only; no additional read is performed after the mutation.</summary>
    public sealed class PlayServUpsertResult
    {
        internal PlayServUpsertResult(string id, bool created) { Id = id; Created = created; }
        public string Id { get; }
        public bool Created { get; }
    }

    /// <summary>Ordered acknowledgments from one atomic transaction, not per-item fan-out results.</summary>
    public sealed class PlayServBulkUpsertResult
    {
        internal PlayServBulkUpsertResult(IReadOnlyList<PlayServUpsertResult> rows, int created, int updated)
        { Rows = rows; Created = created; Updated = updated; }
        public IReadOnlyList<PlayServUpsertResult> Rows { get; }
        public int Created { get; }
        public int Updated { get; }
    }

    public sealed partial class PlayServRecordSet<T>
    {
        /// <summary>
        /// Atomically creates or updates by natural key. Record accepts a typed DTO, anonymous
        /// object or dictionary; only serialized fields are supplied. Managed upserts still
        /// undergo backend create validation. Reuse an idempotency key only for the same request.
        /// No automatic retry or hydration is performed.
        /// </summary>
        public async Task<PlayServUpsertResult> UpsertByNaturalKeyAsync(
            PlayServNaturalKey<T> key, object record, PlayServUpsertMode mode,
            string ifMatch = null, string idempotencyKey = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            PlayServRecordsClient.ValidateWriteHeader(ifMatch, nameof(ifMatch));
            PlayServRecordsClient.ValidateWriteHeader(idempotencyKey, nameof(idempotencyKey));
            var body = _client.BuildUpsertBody(key, record, mode);
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Write, ct);
            return await _client.UpsertAsync(entity.Id, body, ifMatch, idempotencyKey, ct);
        }

        /// <summary>Upserts 1–200 rows in a single transaction. Row fields override defaults.</summary>
        public async Task<PlayServBulkUpsertResult> BulkUpsertAsync(
            IEnumerable<PlayServBulkUpsertRow<T>> rows, PlayServUpsertMode mode,
            object defaults = null, string idempotencyKey = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            PlayServRecordsClient.ValidateWriteHeader(idempotencyKey, nameof(idempotencyKey));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var snapshot = rows.Take(201).ToArray();
            var body = _client.BuildBulkUpsertBody(snapshot, mode, defaults);
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Write, ct);
            return await _client.BulkUpsertAsync(entity.Id, body, snapshot.Length, idempotencyKey, ct);
        }

        /// <summary>Patches an existing natural-key record, optionally requiring the supplied ETag.</summary>
        public async Task<PlayServRecord<T>> PatchByNaturalKeyAsync(
            PlayServNaturalKey<T> key, object patch, string ifMatch = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var query = PlayServRecordsClient.NaturalKeyQuery(key);
            PlayServRecordsClient.ValidateWriteHeader(ifMatch, nameof(ifMatch));
            var body = _client.Json.Serialize(_client.ToRecordFields(patch));
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Write, ct);
            return await _client.PatchByNaturalKeyAsync(this, entity.Id, query, body, ifMatch, ct);
        }

        /// <summary>Deletes an existing natural-key record. Missing records remain typed 404 failures.</summary>
        public async Task DeleteByNaturalKeyAsync(
            PlayServNaturalKey<T> key, string ifMatch = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var query = PlayServRecordsClient.NaturalKeyQuery(key);
            PlayServRecordsClient.ValidateWriteHeader(ifMatch, nameof(ifMatch));
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Write, ct);
            await _client.DeleteByNaturalKeyAsync(entity.Id, query, ifMatch, ct);
        }
    }

    internal sealed partial class PlayServRecordsClient
    {
        internal static void ValidateWriteHeader(string value, string name)
        {
            if (value != null && (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)))
                throw new ArgumentException("Write header must be non-empty and contain no control characters.", name);
        }

        private static string UpsertModeToWire(PlayServUpsertMode mode)
        {
            switch (mode)
            {
                case PlayServUpsertMode.Seed: return "seed";
                case PlayServUpsertMode.Managed: return "managed";
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static string NaturalKeyValue<T>(PlayServNaturalKey<T> key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var value = Convert.ToString(key.Value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(key.Field) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Natural-key field and value are required.", nameof(key));
            return value;
        }

        internal static string NaturalKeyQuery<T>(PlayServNaturalKey<T> key) =>
            "?field=" + Escape(key?.Field) + "&value=" + Escape(NaturalKeyValue(key));

        private Dictionary<string, object> UpsertRow<T>(PlayServNaturalKey<T> key, object record)
        {
            var value = NaturalKeyValue(key);
            return new Dictionary<string, object>
            { ["field"] = key.Field, ["value"] = value, ["record"] = ToRecordFields(record) };
        }

        internal string BuildUpsertBody<T>(PlayServNaturalKey<T> key, object record, PlayServUpsertMode mode)
        {
            var body = UpsertRow(key, record);
            body["mode"] = UpsertModeToWire(mode);
            return _json.Serialize(body);
        }

        internal string BuildBulkUpsertBody<T>(IReadOnlyList<PlayServBulkUpsertRow<T>> rows,
            PlayServUpsertMode mode, object defaults)
        {
            if (rows.Count < 1 || rows.Count > 200)
                throw new ArgumentOutOfRangeException(nameof(rows), "Atomic bulk upsert requires 1–200 rows.");
            var body = new Dictionary<string, object>
            {
                ["mode"] = UpsertModeToWire(mode),
                ["defaults"] = defaults == null ? null : ToRecordFields(defaults),
                ["records"] = rows.Select(row => row == null
                    ? throw new ArgumentException("Bulk upsert rows cannot contain null.", nameof(rows))
                    : UpsertRow(row.Key, row.Record)).ToArray()
            };
            return _json.Serialize(body);
        }

        internal async Task<PlayServUpsertResult> UpsertAsync(string entityId, string body,
            string ifMatch, string idempotencyKey, CancellationToken ct)
        {
            var response = await SendAsync("POST", $"data/tables/{Escape(entityId)}/records:upsert",
                body, ifMatch, idempotencyKey ?? Guid.NewGuid().ToString("D"), ct, entityId);
            return ParseWriteResponse(() => ParseUpsertResult(ParseObject(response.Body, "Upsert")));
        }

        private static TResult ParseWriteResponse<TResult>(Func<TResult> parse)
        {
            try { return parse(); }
            catch (PlayServDataException) { throw; }
            catch (Exception)
            {
                throw new PlayServDataException("Natural-key write response was malformed.", backendCode: "invalid_response");
            }
        }

        private static PlayServUpsertResult ParseUpsertResult(IDictionary<string, object> row)
        {
            var id = GetString(row, "id");
            if (string.IsNullOrWhiteSpace(id) || !TryGet(row, "created", out var created) || !(created is bool))
                throw new PlayServDataException("Upsert acknowledgment must contain id and created.", backendCode: "invalid_response");
            return new PlayServUpsertResult(id, (bool)created);
        }

        internal async Task<PlayServBulkUpsertResult> BulkUpsertAsync(string entityId, string body,
            int count, string idempotencyKey, CancellationToken ct)
        {
            var response = await SendAsync("POST", $"data/tables/{Escape(entityId)}/records:bulk-upsert",
                body, null, idempotencyKey ?? Guid.NewGuid().ToString("D"), ct, entityId);
            return ParseWriteResponse(() =>
            {
                var root = ParseObject(response.Body, "Bulk upsert");
                var rows = GetEnumerable(root, "rows").Select(row => ParseUpsertResult(AsDictionary(row))).ToArray();
                var created = GetNullableInt64(root, "created");
                var updated = GetNullableInt64(root, "updated");
                if (rows.Length != count || created != rows.Count(row => row.Created) || updated != rows.Count(row => !row.Created))
                    throw new PlayServDataException("Bulk upsert acknowledgment has inconsistent row counts.", backendCode: "invalid_response");
                return new PlayServBulkUpsertResult(Array.AsReadOnly(rows), (int)created.Value, (int)updated.Value);
            });
        }

        internal async Task<PlayServRecord<T>> PatchByNaturalKeyAsync<T>(PlayServRecordSet<T> owner,
            string entityId, string query, string body, string ifMatch, CancellationToken ct)
        {
            var response = await SendAsync("PATCH", $"data/tables/{Escape(entityId)}/records:by-natural-key" + query,
                body, ifMatch, null, ct, entityId);
            return ParseWriteResponse(() => ParseRecord(owner, entityId, response.Body, response.ETag, false));
        }

        internal async Task DeleteByNaturalKeyAsync(string entityId, string query, string ifMatch, CancellationToken ct)
        {
            await SendAsync("DELETE", $"data/tables/{Escape(entityId)}/records:by-natural-key" + query,
                null, ifMatch, null, ct, entityId);
        }
    }
}
