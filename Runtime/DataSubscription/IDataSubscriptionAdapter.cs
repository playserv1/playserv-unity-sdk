using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Responses;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Adapter that creates shared entity subscriptions from entity model and mapping function.
    /// </summary>
    public interface IDataSubscriptionAdapter
    {
        /// <summary>
        /// Subscribes to entity by player id and maps backend entity model to client DTO.
        /// </summary>
        /// <typeparam name="TEntity">Raw backend entity model type.</typeparam>
        /// <typeparam name="TDto">Client DTO type.</typeparam>
        /// <param name="playerId">Entity key/player id.</param>
        /// <param name="map">Projection from backend entity to DTO.</param>
        /// <param name="mode">Subscription backend mode (Transport/Polling).</param>
        /// <returns>Shared entity instance.</returns>
        Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(
            string playerId,
            Func<TEntity, TDto> map,
            DataSubscriptionMode mode)
            where TEntity : class
            where TDto : class, new();

        /// <summary>
        /// Sends one simplified data retrieval request by key.
        /// </summary>
        /// <param name="key">Entity key.</param>
        /// <param name="query">GraphQL-like query string.</param>
        /// <param name="variables">Query variables.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>DataGetResponse with Result or Error.</returns>
        Task<DataGetResponse> GetDataByKeyAsync(
            string key,
            string query,
            Dictionary<string, object> variables,
            CancellationToken ct = default);

        /// <summary>
        /// Starts periodic key-based retrieval loop.
        /// </summary>
        /// <param name="key">Entity key.</param>
        /// <param name="query">GraphQL-like query string.</param>
        /// <param name="variables">Query variables.</param>
        /// <param name="onData">Callback invoked for each response.</param>
        /// <param name="onError">Optional callback for transport/runtime errors.</param>
        /// <returns>Disposable handle to stop polling.</returns>
        IDisposable StartDataByKeyPolling(
            string key,
            string query,
            Dictionary<string, object> variables,
            Action<DataGetResponse> onData,
            Action<Exception> onError = null);
    }
}
