using System;

namespace Playserv.Modules
{
    public interface IPlayServModuleServiceProvider
    {
        bool TryGet<TService>(out TService service)
            where TService : class;

        TService Get<TService>()
            where TService : class;

        bool Contains(Type serviceType);
    }
}
