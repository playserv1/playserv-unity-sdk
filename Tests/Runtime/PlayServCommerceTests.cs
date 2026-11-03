using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Commerce;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServCommerceTests
    {
        [Test]
        public void Catalog_list_encodes_filters_and_maps_page_metadata()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"data\":[{\"id\":\"itm_1\",\"sku\":\"coins_500\",\"name\":\"500 coins\",\"status\":\"published\",\"image_url\":\"https://cdn.example/coins.png\",\"accent_color\":\"#f7c948\",\"link_state\":\"linked\",\"mapped_platforms\":[\"steam\"],\"updated_at\":\"2026-08-19T00:00:00Z\"}],\"page\":{\"cursor_next\":\"next\",\"cursor_prev\":null,\"has_more\":true},\"total_estimate\":12}",
                null,
                null));
            var client = CreateClient(http);

            var page = client.ListCatalogAsync(new PlayServCatalogQuery
                {
                    Status = "published",
                    Search = "coins pack",
                    Sort = "updated_at:desc",
                    Cursor = "abc/123",
                    Limit = 25
                }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(page.Items.Count, Is.EqualTo(1));
            Assert.That(page.Items[0].Id, Is.EqualTo("itm_1"));
            Assert.That(page.Items[0].ImageUrl, Does.EndWith("coins.png"));
            Assert.That(page.Items[0].MappedPlatforms, Is.EqualTo(new[] { "steam" }));
            Assert.That(page.Page.CursorNext, Is.EqualTo("next"));
            Assert.That(page.Page.HasMore, Is.True);
            Assert.That(page.TotalEstimate, Is.EqualTo(12));

            var request = http.Requests[0];
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(
                request.RelativePath,
                Is.EqualTo("catalog/items?status=published&q=coins%20pack&sort=updated_at%3Adesc&cursor=abc%2F123&limit=25"));
            Assert.That(request.ClientToken, Is.EqualTo("pk_public"));
            Assert.That(request.BearerToken, Is.EqualTo("player.jwt.value"));
        }

        [Test]
        public void Catalog_get_maps_full_runtime_item()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"id\":\"itm_bundle\",\"sku\":\"starter\",\"name\":\"Starter bundle\",\"description\":\"Welcome\",\"status\":\"published\",\"product_type\":\"bundle\",\"image_url\":null,\"accent_color\":\"#fff\",\"localizations\":{\"uk-UA\":{\"name\":\"Старт\",\"description\":\"Вітаємо\"}},\"bundle_contents\":[{\"item_id\":\"itm_coins\",\"quantity\":5,\"note\":\"bonus\"}],\"mappings\":{\"steam\":{\"sku\":\"steam.starter\",\"sync_status\":\"synced\",\"platform_state\":\"active\",\"platform_product_type\":\"dlc\",\"last_sync_attempted_at\":null,\"last_sync_succeeded_at\":\"2026-08-18T00:00:00Z\",\"localizations\":{},\"flags\":{\"available_in_all_territories\":true,\"available_territories\":[],\"family_sharable\":false,\"content_hosting\":false},\"error\":null,\"status\":\"active\",\"synced_at\":\"2026-08-18T00:00:00Z\"}},\"created_at\":\"2026-08-01T00:00:00Z\",\"updated_at\":\"2026-08-19T00:00:00Z\",\"created_by\":{\"id\":\"usr_1\",\"email\":\"dev@example.com\",\"name\":\"Dev\"},\"updated_by\":null}",
                null,
                null));
            var client = CreateClient(http, withPlayerToken: false);

            var item = client.GetCatalogItemAsync("itm_bundle", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(http.Requests[0].RelativePath, Is.EqualTo("catalog/items/itm_bundle"));
            Assert.That(http.Requests[0].BearerToken, Is.Null);
            Assert.That(item.ProductType, Is.EqualTo("bundle"));
            Assert.That(item.BundleContents[0].ItemId, Is.EqualTo("itm_coins"));
            Assert.That(item.Localizations["uk-UA"].Name, Is.EqualTo("Старт"));
            Assert.That(item.Mappings["steam"].Flags.AvailableInAllTerritories, Is.True);
            Assert.That(item.CreatedBy.Email, Is.EqualTo("dev@example.com"));
        }

        [Test]
        public void Storefront_list_encodes_filters_and_maps_nested_contract()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(
                200,
                "{\"data\":[{\"id\":\"sfr_main\",\"name\":\"Main\",\"description\":null,\"status\":\"live\",\"items\":[{\"item_id\":\"itm_1\",\"position\":0}],\"audience\":{\"preset\":\"all_players\",\"platforms_allowlist\":[\"steam\"],\"countries_allowlist\":[\"UA\"]},\"schedule\":{\"mode\":\"always_on\",\"start_at\":null,\"end_at\":null,\"timezone\":\"UTC\"},\"stats_30d\":{\"revenue\":{\"amount_minor\":1234,\"currency\":\"USD\"},\"impressions\":100,\"conversions\":4,\"revenue_trend_pct\":2.5,\"impressions_trend_pct\":1.5,\"conv_trend_pct\":0.5,\"by_platform\":[{\"platform\":\"steam\",\"revenue\":{\"amount_minor\":1234,\"currency\":\"USD\"}}],\"by_item\":[{\"item_id\":\"itm_1\",\"revenue\":{\"amount_minor\":1234,\"currency\":\"USD\"}}],\"sparkline\":[1,2,3]},\"created_at\":\"2026-08-01T00:00:00Z\",\"updated_at\":\"2026-08-19T00:00:00Z\",\"deleted_at\":null,\"delete_purge_at\":null,\"pending\":null}],\"page\":{\"cursor_next\":null,\"cursor_prev\":\"prev\",\"has_more\":false},\"total_estimate\":1}",
                null,
                null));
            var client = CreateClient(http);

            var page = client.ListStorefrontsAsync(new PlayServStorefrontQuery
                {
                    Status = "live",
                    Audience = "all_players",
                    Search = "main shop",
                    Sort = "name:asc",
                    Cursor = "cursor+1",
                    Limit = 10
                }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(
                http.Requests[0].RelativePath,
                Is.EqualTo("storefronts?status=live&audience=all_players&q=main%20shop&sort=name%3Aasc&cursor=cursor%2B1&limit=10"));
            Assert.That(page.Storefronts.Count, Is.EqualTo(1));
            Assert.That(page.Storefronts[0].Items[0].ItemId, Is.EqualTo("itm_1"));
            Assert.That(page.Storefronts[0].Audience.CountriesAllowlist, Is.EqualTo(new[] { "UA" }));
            Assert.That(page.Storefronts[0].Stats30Days.Revenue.AmountMinor, Is.EqualTo(1234));
            Assert.That(page.Storefronts[0].Stats30Days.ByItem[0].ItemId, Is.EqualTo("itm_1"));
            Assert.That(page.Page.CursorPrevious, Is.EqualTo("prev"));
        }

        [Test]
        public void Problem_details_are_mapped_to_unified_exception()
        {
            var http = new FakeRuntimeHttpClient
            {
                Exception = new PlayServRuntimeHttpException(
                    "not found",
                    404,
                    "{\"code\":\"catalog_item_not_found\",\"detail\":\"Item missing\"}",
                    "catalog_item_not_found",
                    false,
                    problemDetail: "Item missing")
            };
            var client = CreateClient(http);

            var exception = Assert.Throws<PlayServCommerceException>(() =>
                client.GetCatalogItemAsync("itm_missing", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            Assert.That(exception.Service, Is.EqualTo("catalog"));
            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.NotFound));
            Assert.That(exception.UnifiedError.SourceCode, Is.EqualTo("catalog_item_not_found"));
            Assert.That(exception.UnifiedError.HttpStatus, Is.EqualTo(404));
            Assert.That(exception.UnifiedError.RawDetails, Does.Contain("Item missing"));
        }

        [Test]
        public void Invalid_response_is_a_deserialization_failure()
        {
            var http = new FakeRuntimeHttpClient();
            http.Enqueue(new PlayServRuntimeDataResponse(200, "not-json", null, null));
            var client = CreateClient(http);

            var exception = Assert.Throws<PlayServCommerceException>(() =>
                client.ListStorefrontsAsync(null, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            Assert.That(exception.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Deserialization));
            Assert.That(exception.UnifiedError.RawDetails, Is.EqualTo("not-json"));
        }

        [TestCase(0)]
        [TestCase(201)]
        public void Invalid_page_limit_is_rejected_before_network_io(int limit)
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                client.ListCatalogAsync(
                        new PlayServCatalogQuery { Limit = limit },
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void Caller_cancellation_is_not_wrapped()
        {
            var http = new FakeRuntimeHttpClient();
            var client = CreateClient(http);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                client.ListStorefrontsAsync(null, cancellation.Token)
                    .GetAwaiter()
                    .GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        private static PlayServCommerceClient CreateClient(
            FakeRuntimeHttpClient http,
            bool withPlayerToken = true)
        {
            var settings = new PlayServSettings
            {
                BackendServerAddress = "https://platform.example",
                ClientToken = "pk_public",
                RuntimeTokenProvider = withPlayerToken
                    ? new PlayServDelegateRuntimeTokenProvider(
                        _ => Task.FromResult("player.jwt.value"))
                    : null
            };
            return new PlayServCommerceClient(settings, http, new NewtonsoftJsonCodec());
        }

        private sealed class FakeRuntimeHttpClient : IPlayServRuntimeHttpClient
        {
            private readonly Queue<PlayServRuntimeDataResponse> _responses =
                new Queue<PlayServRuntimeDataResponse>();

            public readonly List<PlayServRuntimeDataRequest> Requests =
                new List<PlayServRuntimeDataRequest>();

            public PlayServRuntimeHttpException Exception { get; set; }

            public void Enqueue(PlayServRuntimeDataResponse response) =>
                _responses.Enqueue(response);

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Requests.Add(request);
                if (Exception != null)
                    throw Exception;
                return Task.FromResult(_responses.Dequeue());
            }

            public Task<string> GetLatestVersionAsync(
                string gameId,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> SignInAnonAsync(
                string clientToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerRefreshResponseDto> RefreshAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> LoginExternalAsync(
                string clientToken,
                PlayerExternalLoginRequestDto request,
                string playerAccessToken = null,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task SignOutAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();
        }
    }
}
