using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.Schema;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples.DataSubscription
{
    public sealed class PlayServRecordsSample : MonoBehaviour
    {
        public Task<PlayServDataCapabilities> GetInventoryCapabilitiesAsync(
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            var items = PlayServData.Records<SampleInventoryItem>();
            return forceRefresh
                ? items.RefreshCapabilitiesAsync(ct)
                : items.GetCapabilitiesAsync(ct);
        }

        public async Task<PlayServRecord<SampleInventoryItem>> LoadStarterSwordAsync()
        {
            var items = PlayServData.Records<SampleInventoryItem>();
            var result = await items.LoadOrCreateAsync(
                PlayServNaturalKey<SampleInventoryItem>.For(item => item.Code, "starter-sword"),
                () => new SampleInventoryItem
                {
                    Code = "starter-sword",
                    DisplayName = "Starter Sword",
                    Durability = 100
                });

            result.Record.Value.Durability--;
            await result.Record.SaveAsync();
            return result.Record;
        }

        public Task<PlayServRecordPage<SampleInventoryItem>> LoadDamagedItemsAsync()
        {
            var query = new PlayServRecordQuery<SampleInventoryItem>()
                .Where(item => item.Durability, PlayServQueryOperator.Lt, 25)
                .OrderBy(item => item.Code)
                .WithLimit(50);
            return PlayServData.Records<SampleInventoryItem>().QueryAsync(query);
        }

        public Task<PlayServLoadAllResult<SampleInventoryItem>> LoadAllDamagedItemsAsync(
            int maxRecords = 1_000,
            CancellationToken ct = default)
        {
            var query = new PlayServRecordQuery<SampleInventoryItem>()
                .Where(item => item.Durability < 25)
                .OrderBy(item => item.Code);
            return PlayServData.Records<SampleInventoryItem>().LoadAllAsync(
                query,
                maxRecords,
                pageSize: 200,
                ct: ct);
        }

        public Task<PlayServBulkResult<PlayServRecord<SampleInventoryItem>>> LoadItemsAsync(
            IEnumerable<string> recordIds,
            CancellationToken ct = default) =>
            PlayServData.Records<SampleInventoryItem>().LoadManyAsync(
                recordIds,
                maxConcurrency: 4,
                ct: ct);

        public Task<ISharedCollection<SampleInventoryItem>> SubscribeToDamagedItemsAsync(
            CancellationToken ct = default)
        {
            var query = new PlayServRecordQuery<SampleInventoryItem>()
                .Where(item => item.Durability < 25 && item.DisplayName != null)
                .SelectFields(item => item.Code, item => item.DisplayName, item => item.Durability)
                .WithLimit(50);
            return PlayServData.Records<SampleInventoryItem>().SubscribeAsync(query, ct);
        }

        public Task<ISharedCollection<SampleInventoryItem>> SubscribeToRegionalGuildsAsync(
            CancellationToken ct = default)
        {
            var query = new PlayServRecordQuery<SampleInventoryItem>()
                .Where(item => item.Durability > 0)
                .Or(
                    item => item.Region == "eu",
                    item => item.Region == "us" && item.Durability >= 10)
                .Include(item => item.Guild.Owner)
                .WithLimit(50);
            return PlayServData.Records<SampleInventoryItem>().SubscribeAsync(query, ct);
        }

        public async Task<IPlayServRecordSubscription<SampleInventoryItem>> SubscribeToItemAsync(
            string recordId,
            CancellationToken ct = default)
        {
            var record = await PlayServData.Records<SampleInventoryItem>().LoadAsync(
                recordId,
                null,
                ct);
            var subscription = await record.SubscribeAsync(ct);
            subscription.Changed += change => Debug.Log(
                "Inventory item changed: " + string.Join(", ", change.ChangedFields));
            subscription.Conflict += conflict => Debug.LogWarning(
                "Unsaved inventory edits were replaced by the current backend record.");
            subscription.Terminated += () => Debug.Log(
                record.IsDeleted
                    ? "Inventory item was deleted."
                    : "Inventory item subscription was terminated.");
            return subscription;
        }

        public Task<PlayServSubscriptionCloseResult> CloseDamagedItemsAsync(
            ISharedCollection<SampleInventoryItem> subscription,
            CancellationToken ct = default)
        {
            if (subscription == null)
                throw new System.ArgumentNullException(nameof(subscription));
            return subscription.CloseAsync(ct);
        }

        public Task RefreshRealtimeAsync(
            IPlayServRefreshableSubscription subscription,
            CancellationToken ct = default)
        {
            if (subscription == null)
                throw new System.ArgumentNullException(nameof(subscription));
            return subscription.RefreshAsync(ct);
        }
    }

    public sealed class SampleInventoryItem
    {
        [PlayServField("code")]
        public string Code { get; set; }

        [PlayServField("display_name")]
        public string DisplayName { get; set; }

        [PlayServField("durability")]
        public int Durability { get; set; }

        [PlayServField("region")]
        public string Region { get; set; }

        [PlayServField("guild")]
        public SampleGuild Guild { get; set; }
    }

    public sealed class SampleGuild
    {
        [PlayServField("name")]
        public string Name { get; set; }

        [PlayServField("owner")]
        public SampleGuildOwner Owner { get; set; }
    }

    public sealed class SampleGuildOwner
    {
        [PlayServField("display_name")]
        public string DisplayName { get; set; }
    }
}
