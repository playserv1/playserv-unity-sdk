using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.Status;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private CancellationTokenSource _platformCancellation;
        private DateTimeOffset? _activePlatformStartedAt;
        private string _activePlatformSummary = "none";
        private string _lastPlatformSummary = "none";

        private async Task ExecutePlatformCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "current":
                    await RunPlatformRequestAsync("current", token => PlayServStatus.GetCurrentAsync(token), FormatCurrentStatus);
                    return;
                case "history":
                    await ExecutePlatformHistoryAsync(parts);
                    return;
                case "federation":
                    await RunPlatformRequestAsync("federation", token => PlayServStatus.GetFederationAsync(token), FormatFederation);
                    return;
                case "status":
                    PrintPlatformStatus();
                    return;
                case "cancel":
                    CancelActivePlatform(silent: false);
                    return;
                default:
                    AddLog("Usage: platform <current|history|federation|status|cancel> ...");
                    return;
            }
        }

        private async Task ExecutePlatformHistoryAsync(IReadOnlyList<string> parts)
        {
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    2,
                    new[] { "pop", "days" },
                    Array.Empty<string>(),
                    out var arguments,
                    out var error))
            {
                AddLog(error);
                return;
            }
            if (arguments.Positionals.Count != 0)
            {
                AddLog($"Unexpected platform history argument '{arguments.Positionals[0]}'.");
                return;
            }
            if (!arguments.TryGetInt("days", 90, 1, 365, out var days, out error))
            {
                AddLog(error);
                return;
            }

            var pop = arguments.Get("pop");
            await RunPlatformRequestAsync(
                $"history pop={pop ?? "default"} days={days}",
                token => PlayServStatus.GetHistoryAsync(pop, days, token),
                FormatHistory);
        }

        private async Task RunPlatformRequestAsync<T>(
            string summary,
            Func<CancellationToken, Task<T>> request,
            Func<T, string> formatter)
        {
            if (_platformCancellation != null)
            {
                AddLog($"Platform status operation is already running: {_activePlatformSummary}.");
                return;
            }

            var cancellation = new CancellationTokenSource();
            _platformCancellation = cancellation;
            _activePlatformStartedAt = DateTimeOffset.UtcNow;
            _activePlatformSummary = summary;
            AddLog($"Platform => {summary}");
            try
            {
                var result = await request(cancellation.Token);
                _lastPlatformSummary = formatter(result);
                _status = "Platform status request completed";
                AddLog(_lastPlatformSummary);
            }
            catch (OperationCanceledException)
            {
                _lastPlatformSummary = $"{summary}; canceled";
                _status = "Platform status request canceled";
                AddLog(_lastPlatformSummary);
            }
            catch (PlayServStatusException exception)
            {
                _lastPlatformSummary = $"{summary}; error={FormatError(exception.UnifiedError)}";
                _status = "Platform status request failed";
                AddLog(_lastPlatformSummary);
            }
            finally
            {
                if (ReferenceEquals(_platformCancellation, cancellation))
                {
                    _platformCancellation = null;
                    _activePlatformStartedAt = null;
                    _activePlatformSummary = "none";
                }
                cancellation.Dispose();
            }
        }

        private void PrintPlatformStatus()
        {
            if (_platformCancellation == null)
            {
                AddLog($"Platform status: idle; last={_lastPlatformSummary}");
                return;
            }

            var elapsed = _activePlatformStartedAt.HasValue
                ? (DateTimeOffset.UtcNow - _activePlatformStartedAt.Value).TotalMilliseconds
                : 0d;
            AddLog($"Platform status: running; operation={_activePlatformSummary}; elapsed={elapsed:F0}ms");
        }

        private void CancelActivePlatform(bool silent)
        {
            if (_platformCancellation == null)
            {
                if (!silent)
                    AddLog("No platform status operation is running.");
                return;
            }
            _platformCancellation.Cancel();
            if (!silent)
                AddLog($"Cancellation requested for platform operation '{_activePlatformSummary}'.");
        }

        private static string FormatCurrentStatus(PlayServPlatformStatus status)
        {
            var items = status?.Items ?? Array.Empty<PlayServPlatformStatusItem>();
            var details = items.Length == 0
                ? "none"
                : string.Join(", ", items.Select(item =>
                    $"{item.Pop}/{item.System}={item.CurrentStatus} ({item.SecondsSinceProbe}s)"));
            return $"Platform current: overall={status?.Overall ?? "unknown"}; generated={status?.GeneratedAt:O}; items={details}";
        }

        private static string FormatHistory(PlayServPlatformStatusHistory history)
        {
            var systems = history?.Systems ?? Array.Empty<PlayServPlatformStatusSystemHistory>();
            var details = systems.Length == 0
                ? "none"
                : string.Join(", ", systems.Select(system =>
                {
                    var days = system.Days ?? Array.Empty<PlayServPlatformStatusDay>();
                    var latest = days.LastOrDefault();
                    return latest == null
                        ? $"{system.System}=no-data"
                        : $"{system.System}={latest.AvailabilityPercent}% ({latest.Day})";
                }));
            return $"Platform history: pop={history?.Pop ?? "-"}; days={history?.DaysRequested ?? 0}; systems={details}";
        }

        private static string FormatFederation(PlayServStatusFederation federation)
        {
            var origins = federation?.Origins ?? Array.Empty<Uri>();
            return $"Platform federation: count={origins.Count}; origins={(origins.Count == 0 ? "none" : string.Join(", ", origins))}";
        }

        private async Task ExecuteTableCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "list";
            switch (operation)
            {
                case "list":
                    await ListTablesAsync(parts);
                    return;
                case "get":
                    await GetTableAsync(parts);
                    return;
                default:
                    AddLog("Usage: table <list|get> ...");
                    return;
            }
        }

        private async Task ListTablesAsync(IReadOnlyList<string> parts)
        {
            if (!TryParseTableArguments(parts, 2, out var refresh))
                return;
            var tables = refresh
                ? await GetTerminalTablesAsync(true)
                : await GetTerminalTablesAsync(false);
            AddLog($"Data tables: caller={RecordCaller}; count={tables.Count}; refreshed={refresh}");
            foreach (var table in tables)
                AddLog(FormatTable(table));
        }

        private async Task GetTableAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                AddLog("Usage: table get <idOrName> [--refresh]");
                return;
            }
            if (!TryParseTableArguments(parts, 3, out var refresh))
                return;
            var table = await GetTerminalTableAsync(parts[2], refresh);
            AddLog(FormatTable(table));
        }

        private bool TryParseTableArguments(IReadOnlyList<string> parts, int start, out bool refresh)
        {
            refresh = false;
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    start,
                    Array.Empty<string>(),
                    new[] { "refresh" },
                    out var arguments,
                    out var error))
            {
                AddLog(error);
                return false;
            }
            if (arguments.Positionals.Count != 0)
            {
                AddLog($"Unexpected table argument '{arguments.Positionals[0]}'.");
                return false;
            }
            refresh = arguments.Has("refresh");
            return true;
        }

        internal static string FormatTable(PlayServDataTableInfo table)
        {
            if (table == null)
                return "Data table: none";
            return
                $"Table id={table.EntityId}; name={table.Name}; singleton={table.IsSingleton}; rows={table.RowCount}; " +
                $"updated={table.UpdatedAt:O}; readPolicy={table.ReadPolicy}; {FormatCapabilities(table.Capabilities, false)}";
        }
    }
}
