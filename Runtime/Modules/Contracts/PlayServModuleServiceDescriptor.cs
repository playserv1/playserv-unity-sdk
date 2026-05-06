using System;

namespace Playserv.Modules
{
    public sealed class PlayServModuleServiceDescriptor
    {
        public PlayServModuleServiceDescriptor(
            Type serviceType,
            string name,
            Type implementationType,
            string ownerModuleId)
        {
            ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
            Name = name ?? string.Empty;
            ImplementationType = implementationType;
            OwnerModuleId = string.IsNullOrWhiteSpace(ownerModuleId) ? PlayServModuleIds.Core : ownerModuleId;
        }

        public Type ServiceType { get; }

        public string Name { get; }

        public Type ImplementationType { get; }

        public string OwnerModuleId { get; }

        public string DisplayName =>
            string.IsNullOrEmpty(Name)
                ? ServiceType.FullName
                : $"{ServiceType.FullName} [{Name}]";
    }
}
