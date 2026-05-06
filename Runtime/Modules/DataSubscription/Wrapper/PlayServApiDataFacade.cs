using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiDataFacade : IPlayServDataApi
    {
        private readonly Func<PlayServImplementation> _getRequiredInstance;

        public PlayServApiDataFacade()
            : this(() => PlayServRuntimeHost.RequiredInstance)
        {
        }

        public PlayServApiDataFacade(Func<PlayServImplementation> getRequiredInstance)
        {
            _getRequiredInstance = getRequiredInstance ?? throw new ArgumentNullException(nameof(getRequiredInstance));
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
            _getRequiredInstance().ModuleServices.Get<IDataSubscriptionAdapter>();
    }
}
