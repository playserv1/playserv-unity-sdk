using System;

namespace Playserv.Modules
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PlayServLegacyApiAttribute : Attribute
    {
        public PlayServLegacyApiAttribute(string moduleId, Type contractType, Type implementationType)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                throw new ArgumentException("Module id is required.", nameof(moduleId));

            ModuleId = moduleId.Trim();
            ContractType = contractType ?? throw new ArgumentNullException(nameof(contractType));
            ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
        }

        public string ModuleId { get; }

        public Type ContractType { get; }

        public Type ImplementationType { get; }
    }
}
