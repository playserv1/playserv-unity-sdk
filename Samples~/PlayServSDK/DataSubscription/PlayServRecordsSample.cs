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
        // Keep the operation key AND payload in game-owned state until the outcome is known.
        // An explicit retry after a lost response must reuse both within backend retention.
        public Task<PlayServRecord<SampleInventoryItem>> CreateRewardAsync(
            SampleInventoryItem reward, string operationKey, CancellationToken ct = default) =>
            PlayServData.Records<SampleInventoryItem>().CreateAsync(
                reward, ct: ct, idempotencyKey: operationKey);

        public Task<PlayServBulkCreateResult> CreateRewardsAsync(
            IReadOnlyList<SampleInventoryItem> rewards, string operationKey, CancellationToken ct = default) =>
            PlayServData.Records<SampleInventoryItem>().BulkCreateAsync(
                rewards, ct: ct, idempotencyKey: operationKey);

        public Task<PlayServUpsertResult> SeedStarterSwordAsync(string idempotencyKey, CancellationToken ct = default) =>
            PlayServData.Records<SampleInventoryItem>().UpsertByNaturalKeyAsync(
                PlayServNaturalKey<SampleInventoryItem>.For(item => item.Code, "starter-sword"),
                new SampleInventoryItem { Code = "starter-sword", DisplayName = "Starter Sword", Durability = 100 },
                PlayServUpsertMode.Seed, idempotencyKey: idempotencyKey, ct: ct);

        public Task<PlayServDataCapabilities> GetInventoryCapabilitiesAsync(
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            var items = PlayServData.Records<SampleInventoryItem>();
            return forceRefresh
                ? items.RefreshCapabilitiesAsync(ct)
                : items.GetCapabilitiesAsync(ct);
        }

        // A UI can keep editing the handle while its earlier Save is awaiting a response.
        // The first save acknowledges "Sword"; the newer name remains pending if it wasn't sent.
        public async Task RenameWhileSavingAsync(PlayServRecord<SampleInventoryItem> item, CancellationToken ct = default)
        {
            item.Value.DisplayName = "Sword";
            var save = item.SaveAsync(ct);
            item.Value.DisplayName = "Enchanted Sword";
            await save;
            if (item.HasPendingChanges)
                await item.SaveAsync(ct);
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

        // View filters/sorting/hidden columns are owned by the backend administrator.
        public Task<PlayServRecordPage<SampleInventoryItem>> LoadSavedViewAsync(
            string viewId, string cursor = null, CancellationToken ct = default) =>
            PlayServData.Records<SampleInventoryItem>().QueryViewAsync(viewId, new PlayServPagination(cursor, 50), ct);

        public async Task RenameViewItemAsync(PlayServRecord<SampleInventoryItem> item,
            string name, CancellationToken ct = default)
        {
            // Reload BEFORE editing: View records may omit fields and cannot be saved as-is.
            await item.ReloadAsync(ct);
            item.Value.DisplayName = name;
            await item.SaveAsync(ct);
        }

        // Profile must be an inclusion in the backend schema, not a relation.
        public Task<PlayServRecordPage<SampleInventoryItem>> LoadRegionalItemsAsync(string region) =>
            PlayServData.Records<SampleInventoryItem>().QueryAsync(
                new PlayServRecordQuery<SampleInventoryItem>().Where(item => item.Profile.Region == region));

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

        // Plain reads use native ID batches; each input still has its own result/handle.
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
        [PlayServField("profile")]
        public SampleInventoryProfile Profile { get; set; }

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

    public sealed class SampleInventoryProfile
    {
        [PlayServField("region")]
        public string Region { get; set; }
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
