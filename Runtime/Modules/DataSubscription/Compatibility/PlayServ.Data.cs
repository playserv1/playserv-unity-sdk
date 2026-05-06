#if !PLAYSERV_MODULE_DISABLED_DATA
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;

namespace Playserv.Wrapper
{
    public static partial class PlayServ
    {
        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new() =>
            PlayServData.SelectEntity<TEntity, TDto>(playerId, map, mode);

        public static Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default) =>
            PlayServData.GetDataByKeyAsync(key, query, variables, ct);

        public static IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null) =>
            PlayServData.StartDataByKeyPolling(key, query, variables, onData, onError);
    }
}
#endif
