using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playserv.Commerce;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private PlayServCatalogPage _lastCatalogPage;
        private PlayServCatalogQuery _lastCatalogQuery;
        private string _activeCatalogItemId;
        private PlayServStorefrontPage _lastStorefrontPage;
        private PlayServStorefrontQuery _lastStorefrontQuery;
        private string _activeStorefrontId;

        private async Task ExecuteCatalogCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "list";
            switch (operation)
            {
                case "list":
                    if (!TryParseCatalogQuery(parts, 2, out var query))
                        return;
                    await ListCatalogAsync(query);
                    return;
                case "next":
                    if (_lastCatalogQuery == null || string.IsNullOrWhiteSpace(_lastCatalogPage?.Page?.CursorNext))
                    {
                        AddLog("No next catalog cursor. Run catalog list first.");
                        return;
                    }
                    await ListCatalogAsync(Clone(_lastCatalogQuery, _lastCatalogPage.Page.CursorNext));
                    return;
                case "get":
                    await GetCatalogItemAsync(parts.Count > 2 ? parts[2] : _activeCatalogItemId);
                    return;
                default:
                    AddLog("Usage: catalog <list|next|get> ...");
                    return;
            }
        }

        private async Task ExecuteStorefrontCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "list";
            switch (operation)
            {
                case "list":
                    if (!TryParseStorefrontQuery(parts, 2, out var query))
                        return;
                    await ListStorefrontsAsync(query);
                    return;
                case "next":
                    if (_lastStorefrontQuery == null || string.IsNullOrWhiteSpace(_lastStorefrontPage?.Page?.CursorNext))
                    {
                        AddLog("No next storefront cursor. Run storefront list first.");
                        return;
                    }
                    await ListStorefrontsAsync(Clone(_lastStorefrontQuery, _lastStorefrontPage.Page.CursorNext));
                    return;
                case "get":
                    await GetStorefrontAsync(parts.Count > 2 ? parts[2] : _activeStorefrontId);
                    return;
                default:
                    AddLog("Usage: storefront <list|next|get> ...");
                    return;
            }
        }

        private bool TryParseCatalogQuery(IReadOnlyList<string> parts, int start, out PlayServCatalogQuery query)
        {
            query = null;
            if (!TryParseCommerceArguments(parts, start, includeAudience: false, out var arguments, out var limit))
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

        private bool TryParseStorefrontQuery(IReadOnlyList<string> parts, int start, out PlayServStorefrontQuery query)
        {
            query = null;
            if (!TryParseCommerceArguments(parts, start, includeAudience: true, out var arguments, out var limit))
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
            bool includeAudience,
            out DebugTerminalArguments arguments,
            out int limit)
        {
            var options = includeAudience
                ? new[] { "status", "audience", "search", "sort", "cursor", "limit" }
                : new[] { "status", "search", "sort", "cursor", "limit" };
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    start,
                    options,
                    Array.Empty<string>(),
                    out arguments,
                    out var error))
            {
                limit = 0;
                AddLog(error);
                return false;
            }
            if (arguments.Positionals.Count != 0)
            {
                limit = 0;
                AddLog($"Unexpected commerce argument '{arguments.Positionals[0]}'.");
                return false;
            }
            if (!arguments.TryGetInt("limit", 50, 1, 200, out limit, out error))
            {
                AddLog(error);
                return false;
            }
            return true;
        }

        private async Task ListCatalogAsync(PlayServCatalogQuery query)
        {
            _lastCatalogPage = await PlayServCatalog.ListAsync(query);
            _lastCatalogQuery = Clone(query, query.Cursor);
            _activeCatalogItemId = _lastCatalogPage.Items.FirstOrDefault()?.Id;
            AddLog(
                $"Catalog page: count={_lastCatalogPage.Items.Count}; hasMore={_lastCatalogPage.Page.HasMore}; " +
                $"next={_lastCatalogPage.Page.CursorNext ?? "-"}; previous={_lastCatalogPage.Page.CursorPrevious ?? "-"}; " +
                $"totalEstimate={_lastCatalogPage.TotalEstimate?.ToString() ?? "-"}");
            foreach (var item in _lastCatalogPage.Items)
                AddLog($"Catalog item id={item.Id}; sku={item.Sku}; name='{item.Name}'; status={item.Status}");
        }

        private async Task GetCatalogItemAsync(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                AddLog("Usage: catalog get <itemId>. No active catalog item is retained.");
                return;
            }
            var item = await PlayServCatalog.GetAsync(itemId);
            _activeCatalogItemId = item.Id;
            AddLog(
                $"Catalog item id={item.Id}; sku={item.Sku}; name='{item.Name}'; status={item.Status}; " +
                $"type={item.ProductType}; mappings={item.Mappings?.Count ?? 0}; bundle={item.BundleContents?.Length ?? 0}");
        }

        private async Task ListStorefrontsAsync(PlayServStorefrontQuery query)
        {
            _lastStorefrontPage = await PlayServStorefronts.ListAsync(query);
            _lastStorefrontQuery = Clone(query, query.Cursor);
            _activeStorefrontId = _lastStorefrontPage.Storefronts.FirstOrDefault()?.Id;
            AddLog(
                $"Storefront page: count={_lastStorefrontPage.Storefronts.Count}; hasMore={_lastStorefrontPage.Page.HasMore}; " +
                $"next={_lastStorefrontPage.Page.CursorNext ?? "-"}; previous={_lastStorefrontPage.Page.CursorPrevious ?? "-"}; " +
                $"totalEstimate={_lastStorefrontPage.TotalEstimate?.ToString() ?? "-"}");
            foreach (var storefront in _lastStorefrontPage.Storefronts)
                AddLog($"Storefront id={storefront.Id}; name='{storefront.Name}'; status={storefront.Status}; items={storefront.Items?.Length ?? 0}");
        }

        private async Task GetStorefrontAsync(string storefrontId)
        {
            if (string.IsNullOrWhiteSpace(storefrontId))
            {
                AddLog("Usage: storefront get <storefrontId>. No active storefront is retained.");
                return;
            }
            var storefront = await PlayServStorefronts.GetAsync(storefrontId);
            _activeStorefrontId = storefront.Id;
            AddLog(
                $"Storefront id={storefront.Id}; name='{storefront.Name}'; status={storefront.Status}; " +
                $"items={storefront.Items?.Length ?? 0}; audience={storefront.Audience?.Preset ?? "-"}; " +
                $"schedule={storefront.Schedule?.Mode ?? "-"}");
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
    }
}
