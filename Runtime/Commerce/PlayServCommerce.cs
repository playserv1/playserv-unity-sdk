using System.Threading;
using System.Threading.Tasks;
using Playserv.Commerce;

namespace Playserv.Wrapper
{
    /// <summary>Read-only runtime catalog facade.</summary>
    public static class PlayServCatalog
    {
        public static Task<PlayServCatalogPage> ListAsync(
            PlayServCatalogQuery query = null,
            CancellationToken cancellationToken = default) =>
            PlayServCommerceClient.CreateDefault().ListCatalogAsync(query, cancellationToken);

        public static Task<PlayServCatalogItem> GetAsync(
            string itemId,
            CancellationToken cancellationToken = default) =>
            PlayServCommerceClient.CreateDefault().GetCatalogItemAsync(itemId, cancellationToken);
    }

    /// <summary>Read-only runtime storefront facade.</summary>
    public static class PlayServStorefronts
    {
        public static Task<PlayServStorefrontPage> ListAsync(
            PlayServStorefrontQuery query = null,
            CancellationToken cancellationToken = default) =>
            PlayServCommerceClient.CreateDefault().ListStorefrontsAsync(query, cancellationToken);

        public static Task<PlayServStorefront> GetAsync(
            string storefrontId,
            CancellationToken cancellationToken = default) =>
            PlayServCommerceClient.CreateDefault().GetStorefrontAsync(storefrontId, cancellationToken);
    }
}
