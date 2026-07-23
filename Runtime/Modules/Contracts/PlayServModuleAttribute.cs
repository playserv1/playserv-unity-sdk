using System;

namespace Playserv.Modules
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PlayServModuleAttribute : Attribute
    {
        public PlayServModuleAttribute(string moduleId, Type moduleType, int order)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                throw new ArgumentException("Module id is required.", nameof(moduleId));

            ModuleId = moduleId.Trim();
            ModuleType = moduleType ?? throw new ArgumentNullException(nameof(moduleType));
            Order = order;
        }

        public string ModuleId { get; }

        public Type ModuleType { get; }

        public int Order { get; }
    }
}
