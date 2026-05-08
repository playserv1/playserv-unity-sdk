using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiDataFacade : IPlayServDataApi
    {
        private readonly IPlayServDataRuntimeAccess _runtimeAccess;

        public PlayServApiDataFacade()
            : this(new PlayServDataRuntimeAccess())
        {
        }

        public PlayServApiDataFacade(IPlayServDataRuntimeAccess runtimeAccess)
        {
            _runtimeAccess = runtimeAccess ?? throw new ArgumentNullException(nameof(runtimeAccess));
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode = DataSubscriptionMode.Polling)
            where TEntity : class
            where TDto : class, new()
        {
            return Adapter.SelectEntity(playerId, map, mode);
        }

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            return Adapter.GetDataByKeyAsync(key, query, variables, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return Adapter.StartDataByKeyPolling(key, query, variables, onData, onError);
        }

        private IDataSubscriptionAdapter Adapter =>
            _runtimeAccess.RequiredServices.Get<IDataSubscriptionAdapter>();
    }
}
