using System.Threading;
using System.Threading.Tasks;
using Playserv.Commerce;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>Minimal read-only catalog and storefront flow.</summary>
    public sealed class PlayServCommerceSample : MonoBehaviour
    {
        public async Task LoadLiveStorefrontsAsync(CancellationToken cancellationToken)
        {
            PlayServStorefrontPage page = await PlayServStorefronts.ListAsync(
                new PlayServStorefrontQuery
                {
                    Status = "live",
                    Audience = "all_players",
                    Limit = 25
                },
                cancellationToken);

            foreach (PlayServStorefront storefront in page.Storefronts)
            {
                foreach (PlayServStorefrontItem placement in storefront.Items)
                {
                    PlayServCatalogItem item = await PlayServCatalog.GetAsync(
                        placement.ItemId,
                        cancellationToken);
                    Debug.Log($"{storefront.Name}: {item.Name} ({item.Sku})");
                }
            }
        }
    }
}
