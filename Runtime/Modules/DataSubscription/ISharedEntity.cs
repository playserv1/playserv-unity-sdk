using System;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Represents synchronized server entity projection with change notifications and mutation helpers.
    /// </summary>
    /// <typeparam name="T">Client DTO type.</typeparam>
    public interface ISharedEntity<T>
    {
        /// <summary>
        /// Raised when synchronized value was updated from server or local mutation.
        /// </summary>
        event Action<T> Changed;

        /// <summary>
        /// Raised when subscription reports protocol/domain error.
        /// </summary>
        event Action<DataSubscriptionException> Error;

        /// <summary>
        /// Raised when server terminated this subscription.
        /// </summary>
        event Action Terminated;

        /// <summary>
        /// Gets latest synchronized value.
        /// </summary>
        T Value { get; }

        /// <summary>
        /// Applies local mutation and sends update to server.
        /// </summary>
        /// <param name="action">Mutation action executed on current value.</param>
        void Update(Action<T> action);

        /// <summary>
        /// Applies local mutation and sends update to server asynchronously.
        /// </summary>
        /// <param name="action">Mutation action executed on current value.</param>
        /// <returns>Task completed when mutation request is sent.</returns>
        Task UpdateAsync(Action<T> action);

        /// <summary>
        /// Requests full state refresh from server.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Requests full state refresh from server asynchronously.
        /// </summary>
        /// <returns>Task completed when refresh request is sent.</returns>
        Task RefreshAsync();
    }
}
