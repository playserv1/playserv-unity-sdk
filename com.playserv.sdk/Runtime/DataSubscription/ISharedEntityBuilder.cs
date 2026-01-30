using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Playserv.DataSubscription
{
    public interface ISharedEntityBuilder<T>
    {
        ISharedEntityBuilder<T> Key(object id);
        ISharedEntityBuilder<T> Where(Expression<Func<T, bool>> predicate);
        ISharedEntityBuilder<T> Include<TProp>(Expression<Func<T, TProp>> nav);
        ISharedEntityBuilder<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class, new();
        Task<ISharedEntity<TResult>> BindAsync<TResult>() where TResult : class, new();
        Task<ISharedEntity<T>> BindAsync();
    }
}
