using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playserv.Code;
using Playserv.Commerce;
using Playserv.GameServer;

namespace Playserv.DebugTerminal.GameServer
{
    internal sealed partial class PlayServDebugTerminalGameServerExtension
    {
        private PlayServCatalogPage _lastServerCatalogPage;
        private PlayServCatalogQuery _lastServerCatalogQuery;
        private PlayServStorefrontPage _lastServerStorefrontPage;
        private PlayServStorefrontQuery _lastServerStorefrontQuery;

        private async Task ExecuteServerCodeAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "status";
            if (operation == "status")
            {
                Log($"Server Code: operation={_activeOperation}.");
                return;
            }
            if (operation == "cancel")
            {
                CancelActiveOperation(false);
                return;
            }
            if (operation != "call" && operation != "invoke")
            {
                Log("Usage: server code <call|invoke|status|cancel> ...");
                return;
            }

            var start = 3;
            PlayServFunctionMethod method;
            if (operation == "invoke")
            {
                if (parts.Count <= start || !TryParseMethod(parts[start], out method))
                {
                    Log("Usage: server code invoke <GET|POST|PUT|PATCH|DELETE> <slug> [--body json] [--version tag] [--timeout seconds]");
                    return;
                }
                start++;
            }
            else
            {
                method = PlayServFunctionMethod.Post;
            }

            if (!DebugTerminalArguments.TryParse(
                    parts,
                    start,
                    new[] { "body", "version", "timeout" },
                    Array.Empty<string>(),
                    out var arguments,
                    out var error) ||
                arguments.Positionals.Count != 1 ||
                !arguments.TryGetInt("timeout", 10, 1, 3600, out var timeout, out error))
            {
                Log(error ?? "Cloud Function slug is required.");
                return;
            }
            var body = arguments.Get("body");
            if (body != null && !DebugTerminalArguments.TryParseJson(body, false, out _, out error))
            {
                Log(error);
                return;
            }

            await RunOperationAsync("server code", async token =>
            {
                var result = await PlayServGameServer.Code.InvokeAsync(
                    new PlayServFunctionRequest
                    {
                        Slug = arguments.Positionals[0],
                        Method = method,
                        RawBody = body,
                        Version = arguments.Get("version"),
                        TimeoutSeconds = timeout
                    }, token);
                if (!result.IsSuccess)
                {
                    Log($"Server Code failed: {PlayServDebugTerminal.FormatError(result.Error)}");
                    return;
                }
                var response = result.Response;
                var preview = response?.Body ?? string.Empty;
                if (preview.Length > 512)
                    preview = preview.Substring(0, 512) + "…";
                Log(
                    $"Server Code completed; status={response?.StatusCode}; contentType={response?.ContentType ?? "-"}; " +
                    $"bodyChars={response?.Body?.Length ?? 0}; preview={preview}");
            });
        }

        private async Task ExecuteServerAnalyticsAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "status":
                    Log($"Server analytics: collection={(PlayServGameServer.Analytics.CollectionEnabled ? "enabled" : "disabled")}; pending={PlayServGameServer.Analytics.PendingEventCount}");
                    return;
                case "enable":
                case "disable":
                    PlayServGameServer.Analytics.SetCollectionEnabled(operation == "enable");
                    Log($"Server analytics collection {operation}d.");
                    return;
                case "flush":
                    await RunOperationAsync("server analytics flush", async token =>
                    {
                        await PlayServGameServer.Analytics.FlushAsync(token);
                        Log($"Server analytics flushed; pending={PlayServGameServer.Analytics.PendingEventCount}.");
                    });
                    return;
                case "track":
                    if (!DebugTerminalArguments.TryParse(
                            parts,
                            3,
                            new[] { "player" },
                            Array.Empty<string>(),
                            out var arguments,
                            out var error) || arguments.Positionals.Count < 1 ||
                        !DebugTerminalArguments.TryParsePairs(
                            arguments.Positionals.Skip(1), out var pairs, out error))
                    {
                        Log(error ?? "Usage: server analytics track <event> [key=value ...] [--player playerId]");
                        return;
                    }
                    var parameters = pairs.ToDictionary(
                        pair => pair.Key,
                        pair => DebugTerminalArguments.ParseLooseValue(pair.Value),
                        StringComparer.Ordinal);
                    PlayServGameServer.Analytics.Track(
                        arguments.Positionals[0], parameters, arguments.Get("player"));
                    Log($"Server analytics event queued; name={arguments.Positionals[0]}; parameters={parameters.Count}; player={(arguments.Get("player") == null ? "none" : "set")}.");
                    return;
                default:
                    Log("Usage: server analytics <status|enable|disable|track|flush> ...");
                    return;
            }
        }

        private async Task ExecuteServerCatalogAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "list";
            if (operation == "get")
            {
                if (parts.Count != 4)
                {
                    Log("Usage: server catalog get <itemId>");
                    return;
                }
                await RunOperationAsync("server catalog get", async token =>
                {
                    var item = await PlayServGameServer.Catalog.GetAsync(parts[3], token);
                    Log($"Server catalog item id={item.Id}; sku={item.Sku}; name='{item.Name}'; status={item.Status}; type={item.ProductType}");
                });
                return;
            }

            PlayServCatalogQuery query;
            if (operation == "next")
            {
                if (_lastServerCatalogQuery == null || string.IsNullOrWhiteSpace(_lastServerCatalogPage?.Page?.CursorNext))
                {
                    Log("No next server catalog cursor.");
                    return;
                }
                query = Clone(_lastServerCatalogQuery, _lastServerCatalogPage.Page.CursorNext);
            }
            else if (operation == "list")
            {
                if (!TryParseCatalogQuery(parts, 3, out query))
                    return;
            }
            else
            {
                Log("Usage: server catalog <list|next|get> ...");
                return;
            }
            await RunOperationAsync("server catalog list", async token =>
            {
                _lastServerCatalogPage = await PlayServGameServer.Catalog.ListAsync(query, token);
                _lastServerCatalogQuery = Clone(query, query.Cursor);
                Log($"Server catalog page: count={_lastServerCatalogPage.Items.Count}; hasMore={_lastServerCatalogPage.Page.HasMore}; next={_lastServerCatalogPage.Page.CursorNext ?? "-"}");
                foreach (var item in _lastServerCatalogPage.Items)
                    Log($"Catalog item id={item.Id}; sku={item.Sku}; name='{item.Name}'; status={item.Status}");
            });
        }

        private async Task ExecuteServerStorefrontAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "list";
            if (operation == "get")
            {
                if (parts.Count != 4)
                {
                    Log("Usage: server storefront get <storefrontId>");
                    return;
                }
                await RunOperationAsync("server storefront get", async token =>
                {
                    var storefront = await PlayServGameServer.Storefronts.GetAsync(parts[3], token);
                    Log($"Server storefront id={storefront.Id}; name='{storefront.Name}'; status={storefront.Status}; items={storefront.Items?.Length ?? 0}");
                });
                return;
            }

            PlayServStorefrontQuery query;
            if (operation == "next")
            {
                if (_lastServerStorefrontQuery == null || string.IsNullOrWhiteSpace(_lastServerStorefrontPage?.Page?.CursorNext))
                {
                    Log("No next server storefront cursor.");
                    return;
                }
                query = Clone(_lastServerStorefrontQuery, _lastServerStorefrontPage.Page.CursorNext);
            }
            else if (operation == "list")
            {
                if (!TryParseStorefrontQuery(parts, 3, out query))
                    return;
            }
            else
            {
                Log("Usage: server storefront <list|next|get> ...");
                return;
            }
            await RunOperationAsync("server storefront list", async token =>
            {
                _lastServerStorefrontPage = await PlayServGameServer.Storefronts.ListAsync(query, token);
                _lastServerStorefrontQuery = Clone(query, query.Cursor);
                Log($"Server storefront page: count={_lastServerStorefrontPage.Storefronts.Count}; hasMore={_lastServerStorefrontPage.Page.HasMore}; next={_lastServerStorefrontPage.Page.CursorNext ?? "-"}");
                foreach (var storefront in _lastServerStorefrontPage.Storefronts)
                    Log($"Storefront id={storefront.Id}; name='{storefront.Name}'; status={storefront.Status}; items={storefront.Items?.Length ?? 0}");
            });
        }

        private bool TryParseCatalogQuery(
            IReadOnlyList<string> parts,
            int start,
            out PlayServCatalogQuery query)
        {
            query = null;
            if (!TryParseCommerceArguments(parts, start, false, out var arguments, out var limit))
                return false;
            query = new PlayServCatalogQuery
            {
                Status = arguments.Get("status"),
                Search = arguments.Get("search"),
                Sort = arguments.Get("sort"),
                Cursor = arguments.Get("cursor"),
                Limit = limit
            };
            return true;
        }

        private bool TryParseStorefrontQuery(
            IReadOnlyList<string> parts,
            int start,
            out PlayServStorefrontQuery query)
        {
            query = null;
            if (!TryParseCommerceArguments(parts, start, true, out var arguments, out var limit))
                return false;
            query = new PlayServStorefrontQuery
            {
                Status = arguments.Get("status"),
                Audience = arguments.Get("audience"),
                Search = arguments.Get("search"),
                Sort = arguments.Get("sort"),
                Cursor = arguments.Get("cursor"),
                Limit = limit
            };
            return true;
        }

        private bool TryParseCommerceArguments(
            IReadOnlyList<string> parts,
            int start,
            bool storefront,
            out DebugTerminalArguments arguments,
            out int limit)
        {
            limit = 0;
            var options = storefront
                ? new[] { "status", "audience", "search", "sort", "cursor", "limit" }
                : new[] { "status", "search", "sort", "cursor", "limit" };
            if (!DebugTerminalArguments.TryParse(
                    parts, start, options, Array.Empty<string>(),
                    out arguments, out var error) || arguments.Positionals.Count != 0 ||
                !arguments.TryGetInt("limit", 50, 1, 200, out limit, out error))
            {
                Log(error ?? "Unexpected commerce argument.");
                return false;
            }
            return true;
        }

        private static PlayServCatalogQuery Clone(PlayServCatalogQuery source, string cursor) =>
            new PlayServCatalogQuery
            {
                Status = source.Status,
                Search = source.Search,
                Sort = source.Sort,
                Cursor = cursor,
                Limit = source.Limit
            };

        private static PlayServStorefrontQuery Clone(PlayServStorefrontQuery source, string cursor) =>
            new PlayServStorefrontQuery
            {
                Status = source.Status,
                Audience = source.Audience,
                Search = source.Search,
                Sort = source.Sort,
                Cursor = cursor,
                Limit = source.Limit
            };

        private static bool TryParseMethod(string value, out PlayServFunctionMethod method) =>
            Enum.TryParse(value, true, out method) && Enum.IsDefined(typeof(PlayServFunctionMethod), method);
    }
}
