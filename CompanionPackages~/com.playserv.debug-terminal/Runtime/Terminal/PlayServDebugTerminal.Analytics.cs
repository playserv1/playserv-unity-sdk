using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private async Task ExecuteAnalyticsCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "status":
                    AddLog(
                        $"Analytics: collection={(PlayServAnalytics.CollectionEnabled ? "enabled" : "disabled")}; " +
                        $"pending={PlayServAnalytics.PendingEventCount}; customProvider={PlayServAnalytics.HasCustomProvider}");
                    return;
                case "enable":
                case "disable":
                    PlayServAnalytics.SetCollectionEnabled(operation == "enable");
                    AddLog($"Analytics collection {operation}d.");
                    return;
                case "user":
                    SetAnalyticsUser(parts);
                    return;
                case "property":
                    SetAnalyticsProperty(parts);
                    return;
                case "track":
                    TrackAnalyticsEvent(parts);
                    return;
                case "flush":
                    await PlayServAnalytics.FlushAsync();
                    AddLog($"Analytics flush completed; pending={PlayServAnalytics.PendingEventCount}.");
                    return;
                default:
                    AddLog("Usage: analytics <status|enable|disable|user|property|track|flush> ...");
                    return;
            }
        }

        private void SetAnalyticsUser(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3)
            {
                AddLog("Usage: analytics user <userId|clear>");
                return;
            }

            var userId = string.Equals(parts[2], "clear", StringComparison.OrdinalIgnoreCase)
                ? null
                : parts[2];
            PlayServAnalytics.SetUserId(userId);
            AddLog(userId == null ? "Analytics user ID cleared." : $"Analytics user ID set to '{userId}'.");
        }

        private void SetAnalyticsProperty(IReadOnlyList<string> parts)
        {
            var action = parts.Count > 2 ? parts[2].ToLowerInvariant() : string.Empty;
            switch (action)
            {
                case "set" when parts.Count >= 5:
                    PlayServAnalytics.SetUserProperty(parts[3], string.Join(" ", parts.Skip(4)));
                    AddLog($"Analytics user property '{parts[3]}' set.");
                    return;
                case "remove" when parts.Count == 4:
                    PlayServAnalytics.RemoveUserProperty(parts[3]);
                    AddLog($"Analytics user property '{parts[3]}' removed.");
                    return;
                case "clear" when parts.Count == 3:
                    PlayServAnalytics.ClearUserProperties();
                    AddLog("Analytics user properties cleared.");
                    return;
                default:
                    AddLog("Usage: analytics property <set key value|remove key|clear>");
                    return;
            }
        }

        private void TrackAnalyticsEvent(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                AddLog("Usage: analytics track <event> [key=value ...]");
                return;
            }

            if (!DebugTerminalArguments.TryParsePairs(parts.Skip(3), out var raw, out var error))
            {
                AddLog(error);
                return;
            }

            var parameters = raw.ToDictionary(
                pair => pair.Key,
                pair => DebugTerminalArguments.ParseLooseValue(pair.Value),
                StringComparer.Ordinal);
            PlayServAnalytics.Track(parts[2], parameters);
            AddLog($"Analytics event '{parts[2]}' queued with {parameters.Count} parameter(s).");
        }
    }
}
