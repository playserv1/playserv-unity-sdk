using System;
using System.Threading.Tasks;

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
        /// <returns>Shared entity instance.</returns>
        Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new();
    }
}
