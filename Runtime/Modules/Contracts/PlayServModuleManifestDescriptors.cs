using System;
using System.Collections.Generic;
using System.Reflection;

namespace Playserv.Modules
{
    internal static class PlayServModuleManifestDescriptors
    {
        public static PlayServModuleManifestEntry[] Build()
        {
            var registrations = new List<DescriptorRegistration>();
            var descriptorTypes = GetDescriptorTypes();

            for (var i = 0; i < descriptorTypes.Length; i++)
            {
                var type = descriptorTypes[i];
                if (type == null || type.IsAbstract || !typeof(IPlayServModuleManifestDescriptor).IsAssignableFrom(type))
                    continue;

                var attribute = (PlayServModuleManifestDescriptorAttribute)Attribute.GetCustomAttribute(
                    type,
                    typeof(PlayServModuleManifestDescriptorAttribute));

                if (attribute == null)
                    continue;

                var descriptor = (IPlayServModuleManifestDescriptor)Activator.CreateInstance(type);
                registrations.Add(new DescriptorRegistration(attribute.Order, type.FullName, descriptor.CreateEntry()));
            }

            registrations.Sort(CompareRegistrations);

            var modules = new PlayServModuleManifestEntry[registrations.Count];
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < registrations.Count; i++)
            {
                var module = registrations[i].Module;
                if (!seenIds.Add(module.Id))
                    throw new InvalidOperationException($"Duplicate PlayServ module manifest id: {module.Id}");

                modules[i] = module;
            }

            return modules;
        }

        private static Type[] GetDescriptorTypes()
        {
            try
            {
                return typeof(PlayServModuleManifestDescriptors).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                var compactTypes = new List<Type>();
                for (var i = 0; i < ex.Types.Length; i++)
                {
                    if (ex.Types[i] != null)
                        compactTypes.Add(ex.Types[i]);
                }

                return compactTypes.ToArray();
            }
        }

        private static int CompareRegistrations(DescriptorRegistration left, DescriptorRegistration right)
        {
            var order = left.Order.CompareTo(right.Order);
            return order != 0
                ? order
                : string.Compare(left.TypeName, right.TypeName, StringComparison.Ordinal);
        }

        private sealed class DescriptorRegistration
        {
            public DescriptorRegistration(int order, string typeName, PlayServModuleManifestEntry module)
            {
                Order = order;
                TypeName = typeName;
                Module = module;
            }

            public int Order { get; }

            public string TypeName { get; }

            public PlayServModuleManifestEntry Module { get; }
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class PlayServModuleManifestDescriptorAttribute : Attribute
    {
        public PlayServModuleManifestDescriptorAttribute(int order)
        {
            Order = order;
        }

        public int Order { get; }
    }

    internal interface IPlayServModuleManifestDescriptor
    {
        PlayServModuleManifestEntry CreateEntry();
    }
}
