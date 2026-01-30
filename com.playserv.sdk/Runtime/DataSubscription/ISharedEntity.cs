using System;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;

namespace Playserv.DataSubscription
{
    public interface ISharedEntity<T>
    {
        event Action<T> Changed;
        event Action<DataSubscriptionException> Error;
        event Action Terminated;

        T Value { get; }

        void Update(Action<T> action);
        Task UpdateAsync(Action<T> action);
        void Refresh();
        Task RefreshAsync();
    }
}
