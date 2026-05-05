#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
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
        private static IPlayServDataApi Api => PlayServApiHost.Data;

        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            Api.SelectEntity<TEntity, TDto>(playerId, map, mode);

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

#endif
