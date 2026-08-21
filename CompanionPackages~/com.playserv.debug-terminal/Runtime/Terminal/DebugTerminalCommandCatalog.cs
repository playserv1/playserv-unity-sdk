using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.DebugTerminal
{
    internal static class DebugTerminalCommandCatalog
    {
        private static readonly KeyValuePair<string, string>[] Entries =
        {
            Entry("connect", "connect"),
            Entry("disconnect", "disconnect"),
            Entry("state", "state"),
            Entry("sdk info", "sdk info"),
            Entry("sdk modules", "sdk modules"),
            Entry("sdk latest", "sdk latest [gameId]"),
            Entry("auth", "auth"),
            Entry("auth refresh", "auth refresh"),
            Entry("providers", "providers"),
            Entry("logout", "logout"),
            Entry("identity session", "identity session"),
            Entry("identity login", "identity login <provider> [preserve|recover]"),
            Entry("identity link", "identity link <provider>"),
            Entry("identity unlink", "identity unlink <provider>"),
            Entry("identity merge", "identity merge <current|conflicting>"),
            Entry("bind", "bind <playerId> [polling|transport]"),
            Entry("unbind", "unbind"),
            Entry("rename", "rename <new name>"),
            Entry("addlevel", "addlevel [amount]"),
            Entry("setlevel", "setlevel <value>"),
            Entry("refresh", "refresh"),
            Entry("record create", "record create <nickname> [level]"),
            Entry("record load", "record load <recordId>"),
            Entry("record loadorcreate", "record loadorcreate <nickname> [level]"),
            Entry("record query", "record query [--nickname value] [--min-level n] [--search text] [--sort nickname|level] [--desc] [--limit n] [--cursor value] [--fields nickname,level] [--include path] [--or json]"),
            Entry("record next", "record next"),
            Entry("record subscribe", "record subscribe [query options] [--include relation.path] [--or json]"),
            Entry("record loadall", "record loadall [query options] [--max-records 1-100000] [--page-size 1-200]"),
            Entry("record loadmany", "record loadmany <recordId[,recordId...]> [--concurrency 1-32]"),
            Entry("record populate", "record populate <recordId>"),
            Entry("record populatemany", "record populatemany <recordId[,recordId...]> [--concurrency 1-32]"),
            Entry("record deletebyid", "record deletebyid <recordId> [--etag value]"),
            Entry("record batch", "record batch <status|set|save|delete> ..."),
            Entry("record deleteall", "record deleteall [query options] --confirm <matching|all> [--max-records 1-100000] [--concurrency 1-32]"),
            Entry("record capabilities", "record capabilities [refresh]"),
            Entry("record watch", "record watch"),
            Entry("record unwatch", "record unwatch"),
            Entry("record save", "record save <nickname|level> <value>"),
            Entry("record reload", "record reload"),
            Entry("record delete", "record delete"),
            Entry("record singleton", "record singleton"),
            Entry("record status", "record status"),
            Entry("record close", "record close"),
            Entry("record caller", "record caller <client|server|acting>"),
            Entry("subscription status", "subscription status"),
            Entry("subscription refresh", "subscription refresh [legacy|records|record|all]"),
            Entry("subscription close", "subscription close [legacy|records|record|all]"),
            Entry("subscription errors", "subscription errors"),
            Entry("analytics status", "analytics status"),
            Entry("analytics enable", "analytics enable"),
            Entry("analytics disable", "analytics disable"),
            Entry("analytics user", "analytics user <userId|clear>"),
            Entry("analytics property", "analytics property <set key value|remove key|clear>"),
            Entry("analytics track", "analytics track <event> [key=value ...]"),
            Entry("analytics flush", "analytics flush"),
            Entry("code call", "code call <slug> [--body json] [--query key=value] [--version tag] [--timeout seconds]"),
            Entry("code invoke", "code invoke <GET|POST|PUT|PATCH|DELETE> <slug> [--body json] [--query key=value] [--version tag] [--timeout seconds]"),
            Entry("code bytes", "code bytes <GET|POST|PUT|PATCH|DELETE> <slug> [--input-file path] [--content-type value] [--max-response-bytes n] [request options]"),
            Entry("code download", "code download <GET|POST|PUT|PATCH|DELETE> <slug> <path> [--overwrite] [--max-response-bytes n] [request options]"),
            Entry("code status", "code status"),
            Entry("code cancel", "code cancel"),
            Entry("catalog list", "catalog list [--status value] [--search text] [--sort value] [--cursor value] [--limit 1-200]"),
            Entry("catalog next", "catalog next"),
            Entry("catalog get", "catalog get [itemId]"),
            Entry("storefront list", "storefront list [--status value] [--audience value] [--search text] [--sort value] [--cursor value] [--limit 1-200]"),
            Entry("storefront next", "storefront next"),
            Entry("storefront get", "storefront get [storefrontId]"),
            Entry("match find", "match find <functionSlug> [--matchmaker value] [--wait_ms 0-25000] [--search_age_ms n] [--params json-object]"),
            Entry("match join", "match join <functionSlug> [--matchmaker value] [--wait_ms 0-25000] [--params json-object]"),
            Entry("match launch", "match launch <functionSlug> [--region value]"),
            Entry("match status", "match status"),
            Entry("match cancel", "match cancel"),
            Entry("subevent", "subevent"),
            Entry("unsubevent", "unsubevent"),
            Entry("event subscribe", "event subscribe <typed|raw> [count]"),
            Entry("event unsubscribe", "event unsubscribe <typed|raw> [one|all]"),
            Entry("event subscribe-raw", "event subscribe-raw"),
            Entry("event unsubscribe-raw", "event unsubscribe-raw"),
            Entry("event status", "event status"),
            Entry("platform current", "platform current"),
            Entry("platform history", "platform history [--pop value] [--days 1-365]"),
            Entry("platform federation", "platform federation"),
            Entry("platform status", "platform status"),
            Entry("platform cancel", "platform cancel"),
            Entry("table list", "table list [--refresh]"),
            Entry("table get", "table get <idOrName> [--refresh]"),
            Entry("server configure", "server configure [--backend url] [--heartbeat-sec 1-10] [--timeout-sec n] [--prompt]"),
            Entry("server status", "server status"),
            Entry("server cancel", "server cancel"),
            Entry("server shutdown", "server shutdown"),
            Entry("server realtime", "server realtime <connect|disconnect|status> [options]"),
            Entry("server room", "server room <start|upsert|update|heartbeat|list|status|close> ..."),
            Entry("server match", "server match <find|launch> ..."),
            Entry("server reservation", "server reservation consume <functionSlug> <playerId> [--room value]"),
            Entry("server player", "server player get <playerId>"),
            Entry("server jwt", "server jwt validate --project id --environment env [--issuer value] [--skew-sec n]"),
            Entry("server acting", "server acting <set|clear|status>"),
            Entry("server code", "server code <call|invoke|status|cancel> ..."),
            Entry("server analytics", "server analytics <status|enable|disable|track|flush> ..."),
            Entry("server catalog", "server catalog <list|next|get> ..."),
            Entry("server storefront", "server storefront <list|next|get> ..."),
            Entry("joingroup", "joingroup [group]"),
            Entry("leavegroup", "leavegroup [group]"),
            Entry("publishglobal", "publishglobal <text>"),
            Entry("publishgroup", "publishgroup [group] <text>"),
            Entry("publishuser", "publishuser [userId] <text>"),
            Entry("subrpc", "subrpc"),
            Entry("unsubrpc", "unsubrpc"),
            Entry("rpc named", "rpc named <text>"),
            Entry("rpc args", "rpc args <text>"),
            Entry("rpc expr", "rpc expr <text>"),
            Entry("rpc await", "rpc await <text>"),
            Entry("rpc timeout", "rpc timeout <milliseconds>"),
            Entry("rpc cancel", "rpc cancel"),
            Entry("rpc status", "rpc status"),
            Entry("spawn", "spawn [assetName]"),
            Entry("spawn scope", "spawn scope"),
            Entry("spawn join", "spawn join <group>"),
            Entry("spawn leave", "spawn leave"),
            Entry("spawn despawn", "spawn despawn [spawnId|last]"),
            Entry("keepalive status", "keepalive status"),
            Entry("keepalive reset", "keepalive reset"),
            Entry("errors", "errors [count]"),
            Entry("errors clear", "errors clear"),
            Entry("clear", "clear"),
            Entry("help", "help")
        };

        private static readonly Dictionary<string, string> UsageByPath = Entries
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        internal static readonly string[] TopLevelCommands = Entries
            .Select(pair => pair.Key.Split(' ')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        internal static IEnumerable<string> AllUsages => Entries.Select(pair => pair.Value);

        internal static List<string> GetSuggestions(
            string input,
            Func<string, List<string>> tokenize)
        {
            var normalized = input?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                return TopLevelCommands.ToList();

            var tokens = tokenize(normalized);
            if (tokens.Count == 0)
                return TopLevelCommands.ToList();

            var prefix = string.Join(" ", tokens);
            return Entries
                .Select(pair => pair.Key)
                .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string GetUsage(
            IReadOnlyList<string> tokens,
            IReadOnlyList<string> suggestions)
        {
            if (tokens == null || tokens.Count == 0)
                return string.Empty;

            for (var length = Math.Min(2, tokens.Count); length > 0; length--)
            {
                var path = string.Join(" ", tokens.Take(length));
                if (UsageByPath.TryGetValue(path, out var usage))
                    return usage;
            }

            if (suggestions != null && suggestions.Count == 1 &&
                UsageByPath.TryGetValue(suggestions[0], out var suggestedUsage))
            {
                return suggestedUsage;
            }

            return string.Empty;
        }

        internal static string TryAutocomplete(
            string input,
            Func<string, List<string>> tokenize,
            Func<IReadOnlyList<string>, string> commonPrefix)
        {
            var normalized = input?.Trim() ?? string.Empty;
            var suggestions = GetSuggestions(normalized, tokenize);
            if (suggestions.Count == 0)
                return null;

            if (suggestions.Count == 1)
                return suggestions[0] + " ";

            var prefix = commonPrefix(suggestions);
            return prefix.Length > normalized.Length ? prefix : null;
        }

        private static KeyValuePair<string, string> Entry(string path, string usage) =>
            new KeyValuePair<string, string>(path, usage);
    }
}
