using System;
using System.Threading.Tasks;

namespace Playserv.DataSubscription
{
    public interface IDataSubscriptionAdapter
    {
        Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new();
    }
}