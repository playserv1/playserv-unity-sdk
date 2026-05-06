using System;
using System.Collections.Generic;

namespace Playserv.Modules
{
    public interface IPlayServModuleServiceProvider
    {
        bool TryGet<TService>(out TService service)
            where TService : class;

        TService Get<TService>()
            where TService : class;

        bool TryGet<TService>(string name, out TService service)
            where TService : class;

        bool TryGetCapability<TCapability>(string capabilityName, out TCapability capability)
            where TCapability : class;

        bool Contains(Type serviceType);

        bool Contains(Type serviceType, string name);

        IReadOnlyList<PlayServModuleServiceDescriptor> DescribeServices();
    }
}
