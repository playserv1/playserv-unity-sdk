using System;
using System.Collections.Generic;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Commerce
{
    public sealed class PlayServCatalogQuery
    {
        public string Status { get; set; }
        public string Search { get; set; }
        public string Sort { get; set; }
        public string Cursor { get; set; }
        public int Limit { get; set; } = 50;
    }

    public sealed class PlayServStorefrontQuery
    {
        public string Status { get; set; }
        public string Audience { get; set; }
        public string Search { get; set; }
        public string Sort { get; set; }
        public string Cursor { get; set; }
        public int Limit { get; set; } = 50;
    }

    [Serializable]
    public sealed class PlayServPageInfo
    {
        [PlayServJsonName("cursor_next")]
        public string CursorNext { get; set; }

        [PlayServJsonName("cursor_prev")]
        public string CursorPrevious { get; set; }

        [PlayServJsonName("has_more")]
        public bool HasMore { get; set; }
    }

    public sealed class PlayServCatalogPage
    {
        internal PlayServCatalogPage(
            IReadOnlyList<PlayServCatalogItemSummary> items,
            PlayServPageInfo page,
            long? totalEstimate)
        {
            Items = items ?? Array.Empty<PlayServCatalogItemSummary>();
            Page = page ?? new PlayServPageInfo();
            TotalEstimate = totalEstimate;
        }

        public IReadOnlyList<PlayServCatalogItemSummary> Items { get; }
        public PlayServPageInfo Page { get; }
        public long? TotalEstimate { get; }
    }

    public sealed class PlayServStorefrontPage
    {
        internal PlayServStorefrontPage(
            IReadOnlyList<PlayServStorefront> storefronts,
            PlayServPageInfo page,
            long? totalEstimate)
        {
            Storefronts = storefronts ?? Array.Empty<PlayServStorefront>();
            Page = page ?? new PlayServPageInfo();
            TotalEstimate = totalEstimate;
        }

        public IReadOnlyList<PlayServStorefront> Storefronts { get; }
        public PlayServPageInfo Page { get; }
        public long? TotalEstimate { get; }
    }

    [Serializable]
    public sealed class PlayServCatalogItemSummary
    {
        public string Id { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }

        [PlayServJsonName("image_url")]
        public string ImageUrl { get; set; }

        [PlayServJsonName("accent_color")]
        public string AccentColor { get; set; }

        [PlayServJsonName("link_state")]
        public string LinkState { get; set; }

        [PlayServJsonName("mapped_platforms")]
        public string[] MappedPlatforms { get; set; } = Array.Empty<string>();

        [PlayServJsonName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    [Serializable]
    public sealed class PlayServLocalizedContent
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    [Serializable]
    public sealed class PlayServCatalogBundleEntry
    {
        [PlayServJsonName("item_id")]
        public string ItemId { get; set; }

        public int Quantity { get; set; }
        public string Note { get; set; }
    }

    [Serializable]
    public sealed class PlayServCatalogMappingFlags
    {
        [PlayServJsonName("available_in_all_territories")]
        public bool? AvailableInAllTerritories { get; set; }

        [PlayServJsonName("available_territories")]
        public string[] AvailableTerritories { get; set; } = Array.Empty<string>();

        [PlayServJsonName("family_sharable")]
        public bool? FamilySharable { get; set; }

        [PlayServJsonName("content_hosting")]
        public bool? ContentHosting { get; set; }
    }

    [Serializable]
    public sealed class PlayServCatalogMappingError
    {
        public string Code { get; set; }
        public string Message { get; set; }

        [PlayServJsonName("platform_response_code")]
        public int? PlatformResponseCode { get; set; }

        [PlayServJsonName("retry_after")]
        public DateTimeOffset? RetryAfter { get; set; }

        [PlayServJsonName("hint_url")]
        public string HintUrl { get; set; }
    }

    [Serializable]
    public sealed class PlayServCatalogMapping
    {
        public string Sku { get; set; }

        [PlayServJsonName("sync_status")]
        public string SyncStatus { get; set; }

        [PlayServJsonName("platform_state")]
        public string PlatformState { get; set; }

        [PlayServJsonName("platform_product_type")]
        public string PlatformProductType { get; set; }

        [PlayServJsonName("last_sync_attempted_at")]
        public DateTimeOffset? LastSyncAttemptedAt { get; set; }

        [PlayServJsonName("last_sync_succeeded_at")]
        public DateTimeOffset? LastSyncSucceededAt { get; set; }

        public Dictionary<string, PlayServLocalizedContent> Localizations { get; set; } =
            new Dictionary<string, PlayServLocalizedContent>();

        public PlayServCatalogMappingFlags Flags { get; set; }
        public PlayServCatalogMappingError Error { get; set; }
        public string Status { get; set; }

        [PlayServJsonName("synced_at")]
        public DateTimeOffset? SyncedAt { get; set; }
    }

    [Serializable]
    public sealed class PlayServUserReference
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
    }

    [Serializable]
    public sealed class PlayServCatalogItem
    {
        public string Id { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }

        [PlayServJsonName("product_type")]
        public string ProductType { get; set; }

        [PlayServJsonName("image_url")]
        public string ImageUrl { get; set; }

        [PlayServJsonName("accent_color")]
        public string AccentColor { get; set; }

        public Dictionary<string, PlayServLocalizedContent> Localizations { get; set; } =
            new Dictionary<string, PlayServLocalizedContent>();

        [PlayServJsonName("bundle_contents")]
        public PlayServCatalogBundleEntry[] BundleContents { get; set; } =
            Array.Empty<PlayServCatalogBundleEntry>();

        public Dictionary<string, PlayServCatalogMapping> Mappings { get; set; } =
            new Dictionary<string, PlayServCatalogMapping>();

        [PlayServJsonName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [PlayServJsonName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [PlayServJsonName("created_by")]
        public PlayServUserReference CreatedBy { get; set; }

        [PlayServJsonName("updated_by")]
        public PlayServUserReference UpdatedBy { get; set; }
    }

    [Serializable]
    public sealed class PlayServMoney
    {
        [PlayServJsonName("amount_minor")]
        public long AmountMinor { get; set; }

        public string Currency { get; set; }
    }

    [Serializable]
    public sealed class PlayServStorefrontItem
    {
        [PlayServJsonName("item_id")]
        public string ItemId { get; set; }

        public int Position { get; set; }
    }

    [Serializable]
    public sealed class PlayServStorefrontAudience
    {
        public string Preset { get; set; }

        [PlayServJsonName("platforms_allowlist")]
        public string[] PlatformsAllowlist { get; set; } = Array.Empty<string>();

        [PlayServJsonName("countries_allowlist")]
        public string[] CountriesAllowlist { get; set; } = Array.Empty<string>();
    }

    [Serializable]
    public sealed class PlayServStorefrontSchedule
    {
        public string Mode { get; set; }

        [PlayServJsonName("start_at")]
        public DateTimeOffset? StartsAt { get; set; }

        [PlayServJsonName("end_at")]
        public DateTimeOffset? EndsAt { get; set; }

        public string Timezone { get; set; }
    }

    [Serializable]
    public sealed class PlayServPlatformRevenue
    {
        public string Platform { get; set; }
        public PlayServMoney Revenue { get; set; }
    }

    [Serializable]
    public sealed class PlayServItemRevenue
    {
        [PlayServJsonName("item_id")]
        public string ItemId { get; set; }

        public PlayServMoney Revenue { get; set; }
    }

    [Serializable]
    public sealed class PlayServStorefrontStats
    {
        public PlayServMoney Revenue { get; set; }
        public int Impressions { get; set; }
        public int Conversions { get; set; }

        [PlayServJsonName("revenue_trend_pct")]
        public double? RevenueTrendPercent { get; set; }

        [PlayServJsonName("impressions_trend_pct")]
        public double? ImpressionsTrendPercent { get; set; }

        [PlayServJsonName("conv_trend_pct")]
        public double? ConversionTrendPercent { get; set; }

        [PlayServJsonName("by_platform")]
        public PlayServPlatformRevenue[] ByPlatform { get; set; } =
            Array.Empty<PlayServPlatformRevenue>();

        [PlayServJsonName("by_item")]
        public PlayServItemRevenue[] ByItem { get; set; } =
            Array.Empty<PlayServItemRevenue>();

        public int[] Sparkline { get; set; } = Array.Empty<int>();
    }

    [Serializable]
    public sealed class PlayServPendingState
    {
        public string Kind { get; set; }

        [PlayServJsonName("commit_after")]
        public DateTimeOffset CommitAfter { get; set; }

        [PlayServJsonName("undo_url")]
        public string UndoUrl { get; set; }
    }

    [Serializable]
    public sealed class PlayServStorefront
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public PlayServStorefrontItem[] Items { get; set; } = Array.Empty<PlayServStorefrontItem>();
        public PlayServStorefrontAudience Audience { get; set; }
        public PlayServStorefrontSchedule Schedule { get; set; }

        [PlayServJsonName("stats_30d")]
        public PlayServStorefrontStats Stats30Days { get; set; }

        [PlayServJsonName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [PlayServJsonName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [PlayServJsonName("deleted_at")]
        public DateTimeOffset? DeletedAt { get; set; }

        [PlayServJsonName("delete_purge_at")]
        public DateTimeOffset? DeletePurgeAt { get; set; }

        public PlayServPendingState Pending { get; set; }
    }

    /// <summary>Structured runtime catalog or storefront failure.</summary>
    public sealed class PlayServCommerceException : Exception
    {
        internal PlayServCommerceException(
            string service,
            PlayServError error,
            Exception innerException = null)
            : base(error?.Message ?? "PlayServ commerce request failed.", innerException)
        {
            Service = service ?? string.Empty;
            UnifiedError = error ?? new PlayServError(
                PlayServErrorCode.Unknown,
                "commerce_unknown",
                Message);
        }

        public string Service { get; }
        public PlayServError UnifiedError { get; }
    }
}
