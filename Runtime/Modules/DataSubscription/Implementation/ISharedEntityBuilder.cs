using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Fluent builder for creating shared entity subscriptions.
    /// </summary>
    /// <typeparam name="T">Root entity type.</typeparam>
    public interface ISharedEntityBuilder<T>
    {
        /// <summary>
        /// Sets entity key (id) used in subscription query.
        /// </summary>
        /// <param name="id">Entity key value.</param>
        /// <returns>Current builder instance.</returns>
        ISharedEntityBuilder<T> Key(object id);

        /// <summary>
        /// Forces transport-only subscription flow (DataSubscriptionRequest/DataSubscriptionUpdate).
        /// </summary>
        /// <returns>Current builder instance.</returns>
        ISharedEntityBuilder<T> UseTransport();

        /// <summary>
        /// Forces polling-only subscription flow (DataGet loop).
        /// </summary>
        /// <returns>Current builder instance.</returns>
        ISharedEntityBuilder<T> UsePolling();

        /// <summary>
        /// Retained for source compatibility. Keyed entity reads cannot apply a filter and
        /// <c>BindAsync</c> reports <c>PlayServQueryCapabilityException</c> when this method is used.
        /// </summary>
        /// <param name="predicate">Entity filter expression.</param>
        /// <returns>Current builder instance.</returns>
        [Obsolete("Keyed entity subscriptions cannot apply Where filters. Use PlayServData.Records<T>().SubscribeAsync(query) for filtered collections.")]
        ISharedEntityBuilder<T> Where(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// Expands one direct relation in the keyed entity selection.
        /// </summary>
        /// <typeparam name="TProp">Navigation property type.</typeparam>
        /// <param name="nav">Navigation expression.</param>
        /// <returns>Current builder instance.</returns>
        ISharedEntityBuilder<T> Include<TProp>(Expression<Func<T, TProp>> nav);

        /// <summary>
        /// Projects entity into another DTO type.
        /// </summary>
        /// <typeparam name="TResult">Result DTO type.</typeparam>
        /// <param name="selector">Projection expression.</param>
        /// <returns>Builder for projected DTO type.</returns>
        ISharedEntityBuilder<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class, new();

        /// <summary>
        /// Builds subscription and returns projected shared entity.
        /// </summary>
        /// <typeparam name="TResult">Result DTO type.</typeparam>
        /// <returns>Projected shared entity instance.</returns>
        Task<ISharedEntity<TResult>> BindAsync<TResult>() where TResult : class, new();

        /// <summary>
        /// Builds subscription and returns shared entity of root type.
        /// </summary>
        /// <returns>Shared entity instance.</returns>
        Task<ISharedEntity<T>> BindAsync();
    }
}
