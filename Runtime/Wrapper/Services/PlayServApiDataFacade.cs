#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Responses;
using Playserv.Proxy.Common;
#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServApiDataFacade
    {
        private readonly Func<PlayServImplementation> _getRequiredInstance;

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
            return _getRequiredInstance().SelectEntity(playerId, map, mode);
        }

        public Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default)
        {
            return _getRequiredInstance().GetDataByKeyAsync(key, query, variables, ct);
        }

        public IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null)
        {
            return _getRequiredInstance().StartDataByKeyPolling(key, query, variables, onData, onError);
        }

#if UNITY_5_3_OR_NEWER && !PLAYSERV_DISABLE_SPAWN && !PLAYSERV_DISABLE_EVENTS
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation)
        {
            return _getRequiredInstance().Spawn(assetName, position, rotation);
        }

        public Task<GameObject> Spawn(string assetName, Vector3 position)
        {
            return _getRequiredInstance().Spawn(assetName, position);
        }
#endif
    }
}

#endif
