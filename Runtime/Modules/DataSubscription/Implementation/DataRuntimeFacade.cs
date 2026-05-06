using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;

namespace Playserv.DataSubscription
{
    internal sealed class DataRuntimeFacade
    {
        private readonly IPlayServModuleServiceProvider _moduleServices;

        public DataRuntimeFacade(IPlayServModuleServiceProvider moduleServices)
        {
            _moduleServices = moduleServices ?? throw new ArgumentNullException(nameof(moduleServices));
        }

        public PlayServDataSubscriptionAdapter GetDataSubscriptionAdapter()
        {
            return _moduleServices.Get<PlayServDataSubscriptionAdapter>();
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode)
            where TEntity : class
            where TDto : class, new()
        {
            return DataSubscriptionAdapter.SelectEntity<TEntity, TDto>(playerId, map, mode);
        }

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            return DataSubscriptionAdapter.GetDataByKeyAsync(key, query, variables, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return DataSubscriptionAdapter.StartDataByKeyPolling(key, query, variables, onData, onError);
        }

        private IDataSubscriptionAdapter DataSubscriptionAdapter => _moduleServices.Get<IDataSubscriptionAdapter>();
    }
}
