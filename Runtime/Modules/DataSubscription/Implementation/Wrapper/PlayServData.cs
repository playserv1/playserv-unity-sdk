using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Data-subscription and query-oriented PlayServ SDK surface.
    /// </summary>
    public static class PlayServData
    {
        private static readonly IPlayServDataApi Api = new PlayServApiDataFacade();

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
