using System;
using System.Collections.Generic;

namespace Playserv.Modules
{
    public sealed class PlayServModuleServiceRegistry : IPlayServModuleServiceRegistry
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public void Register<TService>(TService service)
            where TService : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            var serviceType = typeof(TService);
            if (_services.ContainsKey(serviceType))
                throw new InvalidOperationException($"Module service already registered: {serviceType.FullName}");

            _services.Add(serviceType, service);
        }

        public bool TryGet<TService>(out TService service)
            where TService : class
        {
            if (_services.TryGetValue(typeof(TService), out var rawService))
            {
                service = rawService as TService;
                return service != null;
            }

            service = null;
            return false;
        }

        public TService Get<TService>()
            where TService : class
        {
            if (TryGet<TService>(out var service))
                return service;

            throw new InvalidOperationException($"Module service is not registered: {typeof(TService).FullName}");
        }

        public bool Contains(Type serviceType)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            return _services.ContainsKey(serviceType);
        }
    }
}
