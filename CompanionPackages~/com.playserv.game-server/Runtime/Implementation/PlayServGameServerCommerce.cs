using System.Threading;
using System.Threading.Tasks;
using Playserv.Commerce;

namespace Playserv.GameServer
{
    /// <summary>Read-only Catalog facade authenticated with the rotating server key.</summary>
    public sealed class PlayServGameServerCatalog
    {
        internal PlayServGameServerCatalog()
        {
        }

        public Task<PlayServCatalogPage> ListAsync(
            PlayServCatalogQuery query = null,
            CancellationToken cancellationToken = default) =>
            PlayServGameServer.CreateCommerceClient()
                .ListCatalogAsync(query, cancellationToken);

        public Task<PlayServCatalogItem> GetAsync(
            string itemId,
            CancellationToken cancellationToken = default) =>
            PlayServGameServer.CreateCommerceClient()
                .GetCatalogItemAsync(itemId, cancellationToken);
    }

    /// <summary>Read-only Storefront facade authenticated with the rotating server key.</summary>
    public sealed class PlayServGameServerStorefronts
    {
        internal PlayServGameServerStorefronts()
        {
        }

        public Task<PlayServStorefrontPage> ListAsync(
            PlayServStorefrontQuery query = null,
            CancellationToken cancellationToken = default) =>
            PlayServGameServer.CreateCommerceClient()
                .ListStorefrontsAsync(query, cancellationToken);

        public Task<PlayServStorefront> GetAsync(
            string storefrontId,
            CancellationToken cancellationToken = default) =>
            PlayServGameServer.CreateCommerceClient()
                .GetStorefrontAsync(storefrontId, cancellationToken);
    }
}
