using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playserv.Data;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private async Task LoadAllRecordsAsync(IReadOnlyList<string> parts)
        {
            if (!TryParseBulkQuery(
                    parts,
                    2,
                    allowPageSize: true,
                    requireConfirmation: false,
                    out var queryOptions,
                    out var maxRecords,
                    out var pageSize,
                    out _,
                    out _,
                    out var error))
            {
                AddLog(error);
                return;
            }

            var result = await DebugPlayerRecords.LoadAllAsync(
                queryOptions.Build(),
                maxRecords,
                pageSize);
            ReplaceRecordBatch(result.Records);
            _lastRecordBatchSummary =
                $"loadall success={result.Records.Count}; truncated={result.IsTruncated}; next={result.NextCursor ?? "-"}";
            AddLog($"Record batch: {_lastRecordBatchSummary}");
        }

        private async Task LoadManyRecordsAsync(IReadOnlyList<string> parts, bool populate)
        {
            if (!TryParseIdsAndConcurrency(parts, out var ids, out var concurrency, out var error))
            {
                AddLog(error);
                return;
            }

            var result = populate
                ? await DebugPlayerRecords.PopulateManyAsync(ids, maxConcurrency: concurrency)
                : await DebugPlayerRecords.LoadManyAsync(ids, maxConcurrency: concurrency);
            ReplaceRecordBatch(result.Items.Where(item => item.IsSuccess).Select(item => item.Value));
            _lastRecordBatchSummary = FormatBulkResult(populate ? "populatemany" : "loadmany", result);
            AddLog($"Record batch: {_lastRecordBatchSummary}");
            PrintBulkFailures(result);
        }

        private async Task PopulateRecordAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count != 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                AddLog("Usage: record populate <recordId>");
                return;
            }

            var record = await DebugPlayerRecords.PopulateAsync(parts[2]);
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            _activeRecord = record;
            _activeSingleton = null;
            _status = $"Populated Player relation '{record.Id}'";
            AddLog(_status);
            PrintRecord(record);
        }

        private async Task DeleteRecordByIdAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                AddLog("Usage: record deletebyid <recordId> [--etag value]");
                return;
            }
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    3,
                    new[] { "etag" },
                    Array.Empty<string>(),
                    out var arguments,
                    out var error))
            {
                AddLog(error);
                return;
            }
            if (arguments.Positionals.Count != 0)
            {
                AddLog($"Unexpected deletebyid argument '{arguments.Positionals[0]}'.");
                return;
            }

            var recordId = parts[2];
            await DebugPlayerRecords.DeleteByIdAsync(recordId, arguments.Get("etag"));
            if (string.Equals(_activeRecord?.Id, recordId, StringComparison.Ordinal))
            {
                await CloseRecordWatchAsync(logAlreadyClosed: false);
                _activeRecord = null;
            }
            _recordBatch.RemoveAll(record => string.Equals(record.Id, recordId, StringComparison.Ordinal));
            _lastRecordBatchSummary = $"deletebyid id={recordId}";
            AddLog($"Deleted Player record '{recordId}' by ID.");
        }

        private async Task ExecuteRecordBatchCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "status":
                    AddLog($"Record batch: count={_recordBatch.Count}; last={_lastRecordBatchSummary}");
                    foreach (var record in _recordBatch)
                        PrintRecord(record);
                    return;
                case "set":
                    SetRecordBatch(parts);
                    return;
                case "save":
                    await SaveRecordBatchAsync(parts);
                    return;
                case "delete":
                    await DeleteRecordBatchAsync(parts);
                    return;
                default:
                    AddLog("Usage: record batch <status|set|save|delete> ...");
                    return;
            }
        }

        private void SetRecordBatch(IReadOnlyList<string> parts)
        {
            if (_recordBatch.Count == 0)
            {
                AddLog("Record batch is empty. Run record loadall/loadmany/populatemany first.");
                return;
            }
            if (parts.Count < 5)
            {
                AddLog("Usage: record batch set <nickname|level> <value>");
                return;
            }
            var field = parts[3].ToLowerInvariant();
            var value = string.Join(" ", parts.Skip(4));
            if (field == "nickname")
            {
                foreach (var record in _recordBatch.Where(record => !record.IsDeleted))
                    record.Value.Nickname = value;
            }
            else if (field == "level" && int.TryParse(value, out var level))
            {
                foreach (var record in _recordBatch.Where(record => !record.IsDeleted))
                    record.Value.Level = level;
            }
            else
            {
                AddLog(field == "level"
                    ? "Record level must be an integer."
                    : "Record batch field must be nickname or level.");
                return;
            }
            _lastRecordBatchSummary = $"set field={field}; count={_recordBatch.Count}";
            AddLog($"Record batch updated locally; field={field}; count={_recordBatch.Count}");
        }

        private async Task SaveRecordBatchAsync(IReadOnlyList<string> parts)
        {
            if (!TryParseBatchOperation(parts, requireConfirmation: false, out var concurrency))
                return;
            var result = await DebugPlayerRecords.BulkSaveAsync(_recordBatch, concurrency);
            _lastRecordBatchSummary = FormatBulkResult("save", result);
            AddLog($"Record batch: {_lastRecordBatchSummary}");
            PrintBulkFailures(result);
        }

        private async Task DeleteRecordBatchAsync(IReadOnlyList<string> parts)
        {
            if (!TryParseBatchOperation(parts, requireConfirmation: true, out var concurrency))
                return;
            var result = await DebugPlayerRecords.BulkDeleteAsync(_recordBatch, concurrency);
            _lastRecordBatchSummary = FormatBulkResult("delete", result);
            AddLog($"Record batch: {_lastRecordBatchSummary}");
            PrintBulkFailures(result);
        }

        private async Task DeleteAllRecordsAsync(IReadOnlyList<string> parts)
        {
            if (!TryParseBulkQuery(
                    parts,
                    2,
                    allowPageSize: false,
                    requireConfirmation: true,
                    out var queryOptions,
                    out var maxRecords,
                    out _,
                    out var concurrency,
                    out var confirmation,
                    out var error))
            {
                AddLog(error);
                return;
            }

            var result = await DebugPlayerRecords.DeleteAllAsync(
                queryOptions.Build(),
                confirmation,
                maxRecords,
                concurrency);
            ReplaceRecordBatch(result.Items.Select(item => item.Value).Where(value => value != null));
            _lastRecordBatchSummary = FormatBulkResult("deleteall", result);
            AddLog($"Record batch: {_lastRecordBatchSummary}");
            PrintBulkFailures(result);
        }

        private bool TryParseIdsAndConcurrency(
            IReadOnlyList<string> parts,
            out string[] ids,
            out int concurrency,
            out string error)
        {
            ids = Array.Empty<string>();
            concurrency = 4;
            error = null;
            if (parts.Count < 3)
            {
                error = $"Usage: record {parts[1]} <recordId[,recordId...]> [--concurrency 1-32]";
                return false;
            }
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    3,
                    new[] { "concurrency" },
                    Array.Empty<string>(),
                    out var arguments,
                    out error))
                return false;
            if (arguments.Positionals.Count != 0)
            {
                error = $"Unexpected record ID argument '{arguments.Positionals[0]}'.";
                return false;
            }
            if (!arguments.TryGetInt("concurrency", 4, 1, 32, out concurrency, out error))
                return false;
            ids = parts[2]
                .Split(',')
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (ids.Length == 0)
            {
                error = "At least one record ID is required.";
                return false;
            }
            return true;
        }

        private bool TryParseBatchOperation(
            IReadOnlyList<string> parts,
            bool requireConfirmation,
            out int concurrency)
        {
            concurrency = 4;
            if (_recordBatch.Count == 0)
            {
                AddLog("Record batch is empty. Run record loadall/loadmany/populatemany first.");
                return false;
            }
            var values = requireConfirmation ? new[] { "concurrency", "confirm" } : new[] { "concurrency" };
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    3,
                    values,
                    Array.Empty<string>(),
                    out var arguments,
                    out var error))
            {
                AddLog(error);
                return false;
            }
            if (arguments.Positionals.Count != 0)
            {
                AddLog($"Unexpected record batch argument '{arguments.Positionals[0]}'.");
                return false;
            }
            if (!arguments.TryGetInt("concurrency", 4, 1, 32, out concurrency, out error))
            {
                AddLog(error);
                return false;
            }
            if (requireConfirmation && !string.Equals(arguments.Get("confirm"), "batch", StringComparison.OrdinalIgnoreCase))
            {
                AddLog("Bulk delete requires '--confirm batch'.");
                return false;
            }
            return true;
        }

        private static bool TryParseBulkQuery(
            IReadOnlyList<string> parts,
            int start,
            bool allowPageSize,
            bool requireConfirmation,
            out DebugTerminalRecordQueryOptions query,
            out int maxRecords,
            out int pageSize,
            out int concurrency,
            out PlayServDeleteAllConfirmation confirmation,
            out string error)
        {
            query = null;
            maxRecords = requireConfirmation ? 10_000 : 1_000;
            pageSize = 200;
            concurrency = 4;
            confirmation = PlayServDeleteAllConfirmation.MatchingRecords;
            error = null;
            var queryParts = parts.Take(start).ToList();
            for (var index = start; index < parts.Count; index++)
            {
                var token = parts[index];
                var isExtra = token == "--max-records" || token == "--concurrency" ||
                              token == "--confirm" || token == "--page-size";
                if (!isExtra)
                {
                    queryParts.Add(token);
                    continue;
                }
                if (index + 1 >= parts.Count)
                {
                    error = $"Option '{token}' requires a value.";
                    return false;
                }
                var value = parts[++index];
                if (token == "--max-records" &&
                    (!int.TryParse(value, out maxRecords) || maxRecords < 1 || maxRecords > 100_000))
                {
                    error = "--max-records must be between 1 and 100000.";
                    return false;
                }
                if (token == "--page-size")
                {
                    if (!allowPageSize)
                    {
                        error = "--page-size is not supported by record deleteall.";
                        return false;
                    }
                    if (!int.TryParse(value, out pageSize) || pageSize < 1 || pageSize > 200)
                    {
                        error = "--page-size must be between 1 and 200.";
                        return false;
                    }
                }
                if (token == "--concurrency" &&
                    (!int.TryParse(value, out concurrency) || concurrency < 1 || concurrency > 32))
                {
                    error = "--concurrency must be between 1 and 32.";
                    return false;
                }
                if (token == "--confirm")
                {
                    if (!requireConfirmation ||
                        (value != "matching" && value != "all"))
                    {
                        error = "--confirm must be matching or all.";
                        return false;
                    }
                    confirmation = value == "all"
                        ? PlayServDeleteAllConfirmation.AllRecords
                        : PlayServDeleteAllConfirmation.MatchingRecords;
                }
            }

            if (requireConfirmation && !parts.Any(part =>
                    string.Equals(part, "--confirm", StringComparison.OrdinalIgnoreCase)))
            {
                error = "record deleteall requires '--confirm matching' or '--confirm all'.";
                return false;
            }
            return DebugTerminalRecordQueryOptions.TryParse(queryParts, start, out query, out error);
        }

        private void ReplaceRecordBatch(IEnumerable<PlayServRecord<DebugTerminalPlayerDto>> records)
        {
            _recordBatch.Clear();
            _recordBatch.AddRange(records ?? Array.Empty<PlayServRecord<DebugTerminalPlayerDto>>());
            _activeRecord = _recordBatch.FirstOrDefault();
            _activeSingleton = null;
        }

        private static string FormatBulkResult(
            string operation,
            PlayServBulkResult<PlayServRecord<DebugTerminalPlayerDto>> result) =>
            $"{operation} total={result.Items.Count}; succeeded={result.SucceededCount}; failed={result.FailedCount}";

        private void PrintBulkFailures(
            PlayServBulkResult<PlayServRecord<DebugTerminalPlayerDto>> result)
        {
            foreach (var item in result.Items.Where(item => !item.IsSuccess).Take(20))
                AddLog($"Record batch failure: id={item.Key}; {FormatError(item.Error)}");
            if (result.FailedCount > 20)
                AddLog($"Record batch failures truncated; hidden={result.FailedCount - 20}");
        }
    }
}
