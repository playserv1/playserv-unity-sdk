using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Playserv.Modules
{
    public static class PlayServLegacyApiRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Type, ApiRegistration> Registrations =
            new Dictionary<Type, ApiRegistration>();
        private static readonly HashSet<Assembly> ScannedAssemblies = new HashSet<Assembly>();

        public static void Register<TContract, TImplementation>(
            string moduleId,
            Func<TImplementation> factory)
            where TContract : class
            where TImplementation : class, TContract
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            Register(
                moduleId,
                typeof(TContract),
                typeof(TImplementation),
                () => factory());
        }

        public static TContract GetRequired<TContract>()
            where TContract : class
        {
            DiscoverLoadedAssemblies();

            ApiRegistration registration;
            lock (Gate)
            {
                if (!Registrations.TryGetValue(typeof(TContract), out registration))
                {
                    throw new InvalidOperationException(
                        $"PlayServ module API '{typeof(TContract).FullName}' is not installed or enabled.");
                }
            }

            if (!PlayServModuleRegistry.IsSelected(registration.ModuleId))
            {
                throw new InvalidOperationException(
                    $"PlayServ module '{registration.ModuleId}' is disabled for the current project target.");
            }

            return (TContract)registration.GetOrCreate();
        }

        private static void Register(
            string moduleId,
            Type contractType,
            Type implementationType,
            Func<object> factory)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                throw new ArgumentException("Module id is required.", nameof(moduleId));
            if (contractType == null)
                throw new ArgumentNullException(nameof(contractType));
            if (implementationType == null)
                throw new ArgumentNullException(nameof(implementationType));
            if (!contractType.IsAssignableFrom(implementationType))
            {
                throw new ArgumentException(
                    $"{implementationType.FullName} does not implement {contractType.FullName}.",
                    nameof(implementationType));
            }
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            lock (Gate)
            {
                if (Registrations.TryGetValue(contractType, out var existing))
                {
                    if (existing.ImplementationType == implementationType &&
                        string.Equals(existing.ModuleId, moduleId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    throw new InvalidOperationException(
                        $"Legacy API '{contractType.FullName}' is registered by both " +
                        $"{existing.ImplementationType.FullName} and {implementationType.FullName}.");
                }

                Registrations.Add(
                    contractType,
                    new ApiRegistration(moduleId.Trim(), implementationType, factory));
            }
        }

        private static void DiscoverLoadedAssemblies()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                lock (Gate)
                {
                    if (!ScannedAssemblies.Add(assembly))
                        continue;
                }

                PlayServLegacyApiAttribute[] attributes;
                try
                {
                    attributes = assembly
                        .GetCustomAttributes(typeof(PlayServLegacyApiAttribute), inherit: false)
                        .OfType<PlayServLegacyApiAttribute>()
                        .ToArray();
                }
                catch
                {
                    continue;
                }

                for (var attributeIndex = 0; attributeIndex < attributes.Length; attributeIndex++)
                {
                    var attribute = attributes[attributeIndex];
                    Register(
                        attribute.ModuleId,
                        attribute.ContractType,
                        attribute.ImplementationType,
                        () => Activator.CreateInstance(attribute.ImplementationType, nonPublic: true));
                }
            }
        }

        private sealed class ApiRegistration
        {
            private readonly Func<object> _factory;
            private object _instance;

            public ApiRegistration(string moduleId, Type implementationType, Func<object> factory)
            {
                ModuleId = moduleId;
                ImplementationType = implementationType;
                _factory = factory;
            }

            public string ModuleId { get; }

            public Type ImplementationType { get; }

            public object GetOrCreate()
            {
                lock (Gate)
                    return _instance ?? (_instance = _factory());
            }
        }
    }
}
