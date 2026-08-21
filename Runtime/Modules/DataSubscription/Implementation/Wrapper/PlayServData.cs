using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Data-subscription and query-oriented PlayServ SDK surface.
    /// </summary>
    public static class PlayServData
    {
        private static readonly PlayServApiDataFacade Api = new PlayServApiDataFacade();

        /// <summary>
        /// Opens the typed V2 records API and resolves the table from <typeparamref name="T"/>'s CLR name.
        /// </summary>
        public static PlayServRecordSet<T> Records<T>() =>
            new PlayServRecordSet<T>(
                PlayServRecordsClient.CreateDefault(),
                subscribeAsync: Api.SelectTypedCollection<T>,
                subscribeRecordAsync: Api.SelectTypedRecord<T>);

        /// <summary>
        /// Opens the typed V2 records API for an explicit <c>ent_*</c> entity ID.
        /// </summary>
        public static PlayServRecordSet<T> Records<T>(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                throw new ArgumentException("Entity ID is required.", nameof(entityId));
            return new PlayServRecordSet<T>(
                PlayServRecordsClient.CreateDefault(),
                entityId.Trim(),
                Api.SelectTypedCollection<T>,
                Api.SelectTypedRecord<T>);
        }

        /// <summary>Returns the cached caller-visible runtime table catalogue.</summary>
        public static Task<IReadOnlyList<PlayServDataTableInfo>> GetTablesAsync(
            CancellationToken ct = default) =>
            PlayServRecordsClient.CreateDefault().GetTablesAsync(false, ct);

        /// <summary>Forces an atomic catalogue refresh and returns the new snapshot.</summary>
        public static Task<IReadOnlyList<PlayServDataTableInfo>> RefreshTablesAsync(
            CancellationToken ct = default) =>
            PlayServRecordsClient.CreateDefault().GetTablesAsync(true, ct);

        /// <summary>Finds one caller-visible table by <c>ent_*</c> ID or schema name.</summary>
        public static Task<PlayServDataTableInfo> GetTableAsync(
            string idOrName,
            CancellationToken ct = default) =>
            PlayServRecordsClient.CreateDefault().GetTableAsync(idOrName, ct);

        public static Task<PlayServRecord<T>> CreateAsync<T>(
            T value,
            CancellationToken ct = default) =>
            Records<T>().CreateAsync(value, ct);

        public static Task<PlayServRecord<T>> LoadAsync<T>(
            string recordId,
            PlayServLoadOptions options = null,
            CancellationToken ct = default) =>
            Records<T>().LoadAsync(recordId, options, ct);

        public static Task<PlayServLoadOrCreateResult<T>> LoadOrCreateAsync<T>(
            PlayServNaturalKey<T> naturalKey,
            Func<T> factory,
            CancellationToken ct = default) =>
            Records<T>().LoadOrCreateAsync(naturalKey, factory, ct);

        public static Task<PlayServRecordPage<T>> QueryAsync<T>(
            PlayServRecordQuery<T> query = null,
            PlayServPagination pagination = null,
            CancellationToken ct = default) =>
            Records<T>().QueryAsync(query, pagination, ct);

        public static Task<PlayServLoadAllResult<T>> LoadAllAsync<T>(
            PlayServRecordQuery<T> query = null,
            int maxRecords = 1_000,
            int pageSize = 200,
            CancellationToken ct = default) =>
            Records<T>().LoadAllAsync(query, maxRecords, pageSize, ct);

        public static Task<PlayServBulkResult<PlayServRecord<T>>> LoadManyAsync<T>(
            IEnumerable<string> recordIds,
            PlayServLoadOptions options = null,
            int maxConcurrency = 4,
            CancellationToken ct = default) =>
            Records<T>().LoadManyAsync(recordIds, options, maxConcurrency, ct);

        public static Task<PlayServRecord<T>> PopulateAsync<T>(
            string relationRecordId,
            PlayServLoadOptions options = null,
            CancellationToken ct = default) =>
            Records<T>().PopulateAsync(relationRecordId, options, ct);

        public static Task<PlayServBulkResult<PlayServRecord<T>>> PopulateManyAsync<T>(
            IEnumerable<string> relationRecordIds,
            PlayServLoadOptions options = null,
            int maxConcurrency = 4,
            CancellationToken ct = default) =>
            Records<T>().PopulateManyAsync(relationRecordIds, options, maxConcurrency, ct);

        public static Task SaveAsync<T>(
            PlayServRecord<T> record,
            CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            return record.SaveAsync(ct);
        }

        public static Task ReloadAsync<T>(
            PlayServRecord<T> record,
            CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            return record.ReloadAsync(ct);
        }

        public static Task DeleteAsync<T>(
            PlayServRecord<T> record,
            CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            return record.DeleteAsync(ct);
        }

        public static Task DeleteByIdAsync<T>(
            string recordId,
            string etag = null,
            CancellationToken ct = default) =>
            Records<T>().DeleteByIdAsync(recordId, etag, ct);

        public static Task<PlayServBulkResult<PlayServRecord<T>>> BulkSaveAsync<T>(
            IEnumerable<PlayServRecord<T>> records,
            int maxConcurrency = 4,
            CancellationToken ct = default) =>
            Records<T>().BulkSaveAsync(records, maxConcurrency, ct);

        public static Task<PlayServBulkResult<PlayServRecord<T>>> BulkDeleteAsync<T>(
            IEnumerable<PlayServRecord<T>> records,
            int maxConcurrency = 4,
            CancellationToken ct = default) =>
            Records<T>().BulkDeleteAsync(records, maxConcurrency, ct);

        public static Task<PlayServBulkResult<PlayServRecord<T>>> DeleteAllAsync<T>(
            PlayServRecordQuery<T> query,
            PlayServDeleteAllConfirmation confirmation,
            int maxRecords = 10_000,
            int maxConcurrency = 4,
            CancellationToken ct = default) =>
            Records<T>().DeleteAllAsync(
                query,
                confirmation,
                maxRecords,
                maxConcurrency,
                ct);

        public static Task<PlayServSingleton<T>> GetSingletonAsync<T>(
            PlayServLoadOptions options = null,
            CancellationToken ct = default) =>
            Records<T>().GetSingletonAsync(options, ct);

        public static Task SaveAsync<T>(
            PlayServSingleton<T> singleton,
            CancellationToken ct = default)
        {
            if (singleton == null)
                throw new ArgumentNullException(nameof(singleton));
            return singleton.SaveAsync(ct);
        }

        public static Task ReloadAsync<T>(
            PlayServSingleton<T> singleton,
            CancellationToken ct = default)
        {
            if (singleton == null)
                throw new ArgumentNullException(nameof(singleton));
            return singleton.ReloadAsync(ct);
        }

        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            Api.SelectEntity<TEntity, TDto>(playerId, map, mode);

        /// <summary>
        /// Open a live COLLECTION subscription (conventions §21.2): a no-id GraphQL-style query
        /// (e.g. <c>Leaderboard(where: { Kills: { gte: 5 } }) { TankId Kills }</c>) reads the whole
        /// filtered table and pushes it — plus every later row change — as a fresh collection.
        /// Subscribe once → keep getting updates; no polling.
        /// </summary>
        public static Task<ISharedCollection<TItem>> SelectCollection<TItem>(
            string query,
            Dictionary<string, object> variables = null)
            where TItem : class, new() =>
            Api.SelectCollection<TItem>(query, variables);

        public static Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default) =>
            Api.GetDataByKeyAsync(key, query, variables, ct);

        public static IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null) =>
            Api.StartDataByKeyPolling(key, query, variables, onData, onError);
    }
}
