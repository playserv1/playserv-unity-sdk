using System;
using System.Collections.Generic;
using System.Text;

namespace Playserv.Modules
{
    public sealed class PlayServModuleServiceRegistry : IPlayServModuleServiceRegistry
    {
        private readonly Dictionary<ServiceKey, ServiceRegistration> _services =
            new Dictionary<ServiceKey, ServiceRegistration>();

        private string _activeOwnerModuleId = PlayServModuleIds.Core;

        public void Register<TService>(TService service)
            where TService : class
        {
            Register(null, service, PlayServModuleServiceRegistrationPolicy.ThrowIfExists);
        }

        public void Register<TService>(TService service, PlayServModuleServiceRegistrationPolicy policy)
            where TService : class
        {
            Register(null, service, policy);
        }

        public bool TryRegister<TService>(TService service)
            where TService : class
        {
            return TryRegister(null, service);
        }

        public void Register<TService>(string name, TService service)
            where TService : class
        {
            Register(name, service, PlayServModuleServiceRegistrationPolicy.ThrowIfExists);
        }

        public void Register<TService>(string name, TService service, PlayServModuleServiceRegistrationPolicy policy)
            where TService : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            var key = new ServiceKey(typeof(TService), name);
            var registration = CreateRegistration(key, service);

            if (_services.TryGetValue(key, out var existing))
            {
                if (policy == PlayServModuleServiceRegistrationPolicy.Replace)
                {
                    _services[key] = registration;
                    return;
                }

                throw new InvalidOperationException(BuildDuplicateRegistrationMessage(key, existing, registration));
            }

            _services.Add(key, registration);
        }

        public bool TryRegister<TService>(string name, TService service)
            where TService : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            var key = new ServiceKey(typeof(TService), name);
            if (_services.ContainsKey(key))
                return false;

            _services.Add(key, CreateRegistration(key, service));
            return true;
        }

        public void RegisterCapability<TCapability>(string capabilityName, TCapability capability)
            where TCapability : class
        {
            Register(capabilityName, capability);
        }

        public bool TryGet<TService>(out TService service)
            where TService : class
        {
            return TryGet(null, out service);
        }

        public bool TryGet<TService>(string name, out TService service)
            where TService : class
        {
            var key = new ServiceKey(typeof(TService), name);
            if (_services.TryGetValue(key, out var registration))
            {
                service = registration.Service as TService;
                return service != null;
            }

            service = null;
            return false;
        }

        public bool TryGetCapability<TCapability>(string capabilityName, out TCapability capability)
            where TCapability : class
        {
            return TryGet(capabilityName, out capability);
        }

        public TService Get<TService>()
            where TService : class
        {
            if (TryGet<TService>(out var service))
                return service;

            throw new InvalidOperationException(BuildMissingServiceMessage(typeof(TService), null));
        }

        public bool Contains(Type serviceType)
        {
            return Contains(serviceType, null);
        }

        public bool Contains(Type serviceType, string name)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            return _services.ContainsKey(new ServiceKey(serviceType, name));
        }

        public IReadOnlyList<PlayServModuleServiceDescriptor> DescribeServices()
        {
            var result = new List<PlayServModuleServiceDescriptor>(_services.Count);
            foreach (var registration in _services.Values)
                result.Add(registration.Descriptor);

            result.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal));
            return result;
        }

        internal IDisposable BeginModuleRegistration(string moduleId)
        {
            var previousOwner = _activeOwnerModuleId;
            _activeOwnerModuleId = string.IsNullOrWhiteSpace(moduleId) ? PlayServModuleIds.Core : moduleId;
            return new RegistrationScope(this, previousOwner);
        }

        private ServiceRegistration CreateRegistration(ServiceKey key, object service)
        {
            var descriptor = new PlayServModuleServiceDescriptor(
                key.ServiceType,
                key.Name,
                service.GetType(),
                _activeOwnerModuleId);
            return new ServiceRegistration(service, descriptor);
        }

        private string BuildDuplicateRegistrationMessage(
            ServiceKey key,
            ServiceRegistration existing,
            ServiceRegistration replacement)
        {
            return
                $"Module service already registered: {key.DisplayName}. " +
                $"Existing owner={existing.Descriptor.OwnerModuleId}, implementation={existing.Descriptor.ImplementationType.FullName}. " +
                $"New owner={replacement.Descriptor.OwnerModuleId}, implementation={replacement.Descriptor.ImplementationType.FullName}. " +
                "Use TryRegister or Register(..., PlayServModuleServiceRegistrationPolicy.Replace) for intentional overrides.";
        }

        private string BuildMissingServiceMessage(Type serviceType, string name)
        {
            var key = new ServiceKey(serviceType, name);
            var builder = new StringBuilder();
            builder.Append("Module service is not registered: ");
            builder.Append(key.DisplayName);

            var descriptors = DescribeServices();
            if (descriptors.Count == 0)
                return builder.ToString();

            builder.Append(". Registered services: ");
            for (var i = 0; i < descriptors.Count; i++)
            {
                if (i > 0)
                    builder.Append("; ");

                builder.Append(descriptors[i].DisplayName);
                builder.Append(" by ");
                builder.Append(descriptors[i].OwnerModuleId);
            }

            return builder.ToString();
        }

        private readonly struct ServiceKey : IEquatable<ServiceKey>
        {
            public ServiceKey(Type serviceType, string name)
            {
                ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
                Name = NormalizeName(name);
            }

            public Type ServiceType { get; }

            public string Name { get; }

            public string DisplayName =>
                string.IsNullOrEmpty(Name)
                    ? ServiceType.FullName
                    : $"{ServiceType.FullName} [{Name}]";

            public bool Equals(ServiceKey other)
            {
                return ServiceType == other.ServiceType &&
                       string.Equals(Name, other.Name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ServiceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((ServiceType != null ? ServiceType.GetHashCode() : 0) * 397) ^
                           StringComparer.Ordinal.GetHashCode(Name);
                }
            }

            private static string NormalizeName(string name)
            {
                return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            }
        }

        private sealed class ServiceRegistration
        {
            public ServiceRegistration(object service, PlayServModuleServiceDescriptor descriptor)
            {
                Service = service ?? throw new ArgumentNullException(nameof(service));
                Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            }

            public object Service { get; }

            public PlayServModuleServiceDescriptor Descriptor { get; }
        }

        private sealed class RegistrationScope : IDisposable
        {
            private readonly PlayServModuleServiceRegistry _registry;
            private readonly string _previousOwner;
            private bool _disposed;

            public RegistrationScope(PlayServModuleServiceRegistry registry, string previousOwner)
            {
                _registry = registry ?? throw new ArgumentNullException(nameof(registry));
                _previousOwner = previousOwner;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _registry._activeOwnerModuleId = _previousOwner;
                _disposed = true;
            }
        }
    }
}
