using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Common;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Commerce
{
    internal sealed class PlayServCommerceClient
    {
        private const int MaxPageSize = 200;

        private readonly PlayServSettings _settings;
        private readonly IPlayServRuntimeHttpClient _http;
        private readonly IJsonCodec _json;
        private readonly bool _requiresClientToken;

        internal PlayServCommerceClient(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient http,
            IJsonCodec json,
            bool requiresClientToken = true)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _requiresClientToken = requiresClientToken;
        }

        internal static PlayServCommerceClient CreateDefault()
        {
            var settings = PlayServ.Settings;
            var json = new NewtonsoftJsonCodec();
            var http = PlayServRuntimeHttpClientResolver.Create(
                new PlayServHttpModuleContext(settings.ToRuntimeSettings(), json));
            return new PlayServCommerceClient(settings, http, json);
        }

        internal async Task<PlayServCatalogPage> ListCatalogAsync(
            PlayServCatalogQuery query,
            CancellationToken cancellationToken)
        {
            query = query ?? new PlayServCatalogQuery();
            ValidateLimit(query.Limit, nameof(query));
            var path = BuildPath("catalog/items", new[]
            {
                Pair("status", query.Status),
                Pair("q", query.Search),
                Pair("sort", query.Sort),
                Pair("cursor", query.Cursor),
                Pair("limit", query.Limit.ToString())
            });
            var page = await GetAsync<PageWire<PlayServCatalogItemSummary>>(
                "catalog",
                path,
                cancellationToken);
            return new PlayServCatalogPage(
                page.data ?? Array.Empty<PlayServCatalogItemSummary>(),
                page.page,
                page.total_estimate);
        }

        internal Task<PlayServCatalogItem> GetCatalogItemAsync(
            string itemId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Catalog item ID is required.", nameof(itemId));
            return GetAsync<PlayServCatalogItem>(
                "catalog",
                "catalog/items/" + Uri.EscapeDataString(itemId.Trim()),
                cancellationToken);
        }

        internal async Task<PlayServStorefrontPage> ListStorefrontsAsync(
            PlayServStorefrontQuery query,
            CancellationToken cancellationToken)
        {
            query = query ?? new PlayServStorefrontQuery();
            ValidateLimit(query.Limit, nameof(query));
            var path = BuildPath("storefronts", new[]
            {
                Pair("status", query.Status),
                Pair("audience", query.Audience),
                Pair("q", query.Search),
                Pair("sort", query.Sort),
                Pair("cursor", query.Cursor),
                Pair("limit", query.Limit.ToString())
            });
            var page = await GetAsync<PageWire<PlayServStorefront>>(
                "storefronts",
                path,
                cancellationToken);
            return new PlayServStorefrontPage(
                page.data ?? Array.Empty<PlayServStorefront>(),
                page.page,
                page.total_estimate);
        }

        internal Task<PlayServStorefront> GetStorefrontAsync(
            string storefrontId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(storefrontId))
                throw new ArgumentException("Storefront ID is required.", nameof(storefrontId));
            return GetAsync<PlayServStorefront>(
                "storefronts",
                "storefronts/" + Uri.EscapeDataString(storefrontId.Trim()),
                cancellationToken);
        }

        private async Task<T> GetAsync<T>(
            string service,
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureConfigured(service);

            var bearer = _requiresClientToken
                ? await ResolvePlayerTokenForClientAsync(service, cancellationToken)
                : null;

            PlayServRuntimeDataResponse response;
            try
            {
                response = await _http.SendDataAsync(new PlayServRuntimeDataRequest
                {
                    Method = "GET",
                    RelativePath = relativePath,
                    ClientToken = _requiresClientToken ? _settings.ClientToken : null,
                    RequiresClientToken = _requiresClientToken,
                    BearerToken = bearer
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServRuntimeHttpException exception)
            {
                throw new PlayServCommerceException(service, MapHttpError(exception), exception);
            }
            catch (Exception exception)
            {
                throw new PlayServCommerceException(
                    service,
                    new PlayServError(
                        PlayServErrorCode.Network,
                        "commerce_transport_failed",
                        $"PlayServ {service} request failed before a response was received.",
                        retryable: true,
                        rawDetails: exception.Message),
                    exception);
            }

            if (response == null || string.IsNullOrWhiteSpace(response.Body))
            {
                throw new PlayServCommerceException(
                    service,
                    new PlayServError(
                        PlayServErrorCode.InvalidResponse,
                        "commerce_response_missing",
                        $"PlayServ {service} response body is empty.",
                        httpStatus: response?.StatusCode));
            }

            try
            {
                var value = _json.Deserialize<T>(response.Body);
                if (value == null)
                    throw new InvalidOperationException("Deserialized response is null.");
                return value;
            }
            catch (PlayServCommerceException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlayServCommerceException(
                    service,
                    new PlayServError(
                        PlayServErrorCode.Deserialization,
                        "commerce_response_deserialization_failed",
                        $"PlayServ {service} response could not be deserialized.",
                        httpStatus: response.StatusCode,
                        rawDetails: response.Body),
                    exception);
            }
        }

        private async Task<string> ResolvePlayerTokenAsync(CancellationToken cancellationToken)
        {
            if (_settings.RuntimeTokenProvider != null)
                return await _settings.RuntimeTokenProvider.GetTokenAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(_settings.PlayerAccessToken)
                ? null
                : _settings.PlayerAccessToken;
        }

        private async Task<string> ResolvePlayerTokenForClientAsync(
            string service,
            CancellationToken cancellationToken)
        {
            try
            {
                return await ResolvePlayerTokenAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlayServCommerceException(
                    service,
                    new PlayServError(
                        PlayServErrorCode.Unauthorized,
                        "runtime_token_provider_failed",
                        "The runtime player token provider failed.",
                        rawDetails: exception.Message),
                    exception);
            }
        }

        private void EnsureConfigured(string service)
        {
            if (_requiresClientToken && string.IsNullOrWhiteSpace(_settings.ClientToken))
            {
                throw new InvalidOperationException(
                    $"PlayServ.Settings.ClientToken must contain a public pk_* token before using {service}.");
            }
            if (_requiresClientToken)
                PlayServCredentialPolicy.NormalizeClientToken(_settings.ClientToken);
            if (string.IsNullOrWhiteSpace(_settings.BackendServerAddress))
                throw new InvalidOperationException($"PlayServ.Settings.BackendServerAddress is required before using {service}.");
        }

        private static PlayServError MapHttpError(PlayServRuntimeHttpException exception) =>
            PlayServError.FromHttp(
                exception.StatusCode,
                exception.BackendCode,
                FirstNonEmpty(exception.ProblemDetail, exception.Message),
                exception.IsNetworkError,
                exception.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0,
                exception.ResponseBody);

        private static void ValidateLimit(int limit, string argumentName)
        {
            if (limit < 1 || limit > MaxPageSize)
            {
                throw new ArgumentOutOfRangeException(
                    argumentName,
                    limit,
                    $"Commerce page limit must be between 1 and {MaxPageSize}.");
            }
        }

        private static KeyValuePair<string, string> Pair(string key, string value) =>
            new KeyValuePair<string, string>(key, value);

        private static string BuildPath(
            string basePath,
            IEnumerable<KeyValuePair<string, string>> values)
        {
            var query = values
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => string.Concat(
                    Uri.EscapeDataString(pair.Key),
                    "=",
                    Uri.EscapeDataString(pair.Value.Trim())))
                .ToArray();
            return query.Length == 0
                ? basePath
                : basePath + "?" + string.Join("&", query);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "PlayServ commerce request failed.";
        }

        [Serializable]
        private sealed class PageWire<T>
        {
            public T[] data;
            public PlayServPageInfo page;
            public long? total_estimate;
        }
    }
}
