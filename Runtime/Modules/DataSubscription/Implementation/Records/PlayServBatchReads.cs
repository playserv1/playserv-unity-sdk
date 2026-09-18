using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.Data
{
    public sealed partial class PlayServRecordSet<T>
    {
        private async Task<PlayServBulkResult<PlayServRecord<T>>> LoadManyBatchedAsync(
            IReadOnlyList<string> ids, int maxConcurrency, CancellationToken ct)
        {
            ValidateConcurrency(maxConcurrency);
            ct.ThrowIfCancellationRequested();
            var unique = ids.Distinct(StringComparer.Ordinal).ToArray();
            var chunks = new List<string[]>();
            for (var offset = 0; offset < unique.Length; offset += 200)
                chunks.Add(unique.Skip(offset).Take(200).ToArray());

            var batches = await ExecuteBulkAsync(chunks, chunk => chunk[0], LoadIdChunkAsync, maxConcurrency, ct);
            var found = new Dictionary<string, PlayServRecord<T>>(StringComparer.Ordinal);
            var errors = new Dictionary<string, Exception>(StringComparer.Ordinal);
            for (var index = 0; index < chunks.Count; index++)
            {
                var batch = batches.Items[index];
                foreach (var id in chunks[index])
                {
                    if (!batch.IsSuccess) errors[id] = batch.Exception;
                    else if (batch.Value.TryGetValue(id, out var record)) found[id] = record;
                    else errors[id] = new PlayServRecordNotFoundException(
                        "The requested record is missing or is not visible to this caller.", "record_not_found", null, null);
                }
            }

            var results = new PlayServBulkItemResult<PlayServRecord<T>>[ids.Count];
            for (var index = 0; index < ids.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var id = ids[index];
                if (!errors.TryGetValue(id, out var error))
                {
                    try
                    {
                        // Duplicate input IDs must not share mutable handles or nested values.
                        results[index] = new PlayServBulkItemResult<PlayServRecord<T>>(
                            id, _client.CloneLoadedRecord(found[id]), PlayServError.None, null);
                        continue;
                    }
                    catch (Exception exception) { error = exception; }
                }
                results[index] = new PlayServBulkItemResult<PlayServRecord<T>>(id, null, ToBulkError(error), error);
            }
            return new PlayServBulkResult<PlayServRecord<T>>(results);
        }

        private async Task<Dictionary<string, PlayServRecord<T>>> LoadIdChunkAsync(string[] ids, CancellationToken ct)
        {
            var entity = await ResolveForAccessAsync(false, PlayServDataAccessOperation.Read, ct);
            var query = new PlayServRecordQuery<T>().Where("id", PlayServQueryOperator.In, ids).WithLimit(200);
            var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            var records = new Dictionary<string, PlayServRecord<T>>(StringComparer.Ordinal);
            string cursor = null;
            for (var pageNumber = 0; pageNumber <= ids.Length; pageNumber++)
            {
                var page = await _client.QueryAsync(this, entity, query, new PlayServPagination(cursor, 200), ct,
                    requireBatchEnvelope: true);
                foreach (var record in page.Records)
                {
                    if (!wanted.Contains(record.Id) || records.ContainsKey(record.Id))
                        throw InvalidBatchPage();
                    records.Add(record.Id, record);
                }
                if (!page.HasMore) return records;
                cursor = page.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor) || !cursors.Add(cursor)) throw InvalidBatchPage();
            }
            throw InvalidBatchPage();
        }

        private static PlayServDataException InvalidBatchPage() => new PlayServDataException(
            "Batch query returned inconsistent records or pagination.", backendCode: "invalid_response");
    }

    internal sealed partial class PlayServRecordsClient
    {
        internal PlayServRecord<T> CloneLoadedRecord<T>(PlayServRecord<T> record)
        {
            var fields = ParseSnapshot(record.CanonicalSnapshot);
            return new PlayServRecord<T>(record.OwnerSet, this, record.Id, record.EntityId,
                ConvertRecordValue<T>(fields), record.CreatedAt, record.UpdatedAt, record.Owner,
                record.ETag, record.CanonicalSnapshot, record.IsPartial);
        }
    }
}
