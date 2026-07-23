using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Playserv.Proxy.Common
{
    public static class PlayServLocalExecutionFactoryRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Type, FactoryRegistration> Registrations =
            new Dictionary<Type, FactoryRegistration>();
        private static readonly HashSet<Assembly> ScannedAssemblies = new HashSet<Assembly>();

        public static void Register<TImplementation>(
            int priority,
            Func<TImplementation> factory)
            where TImplementation : class, ILocalCommandExecution
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            Register(typeof(TImplementation), priority, () => factory());
        }

        public static ILocalCommandExecution Create()
        {
            DiscoverLoadedAssemblies();

            FactoryRegistration registration;
            lock (Gate)
            {
                registration = Registrations.Values
                    .OrderByDescending(item => item.Priority)
                    .ThenBy(item => item.ImplementationType.FullName, StringComparer.Ordinal)
                    .FirstOrDefault();
            }

            return registration?.Factory();
        }

        private static void Register(
            Type implementationType,
            int priority,
            Func<ILocalCommandExecution> factory)
        {
            if (implementationType == null)
                throw new ArgumentNullException(nameof(implementationType));
            if (!typeof(ILocalCommandExecution).IsAssignableFrom(implementationType))
            {
                throw new ArgumentException(
                    $"{implementationType.FullName} does not implement {nameof(ILocalCommandExecution)}.",
                    nameof(implementationType));
            }
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            lock (Gate)
            {
                if (Registrations.TryGetValue(implementationType, out var existing))
                {
                    if (existing.Priority == priority)
                        return;

                    throw new InvalidOperationException(
                        $"Local execution factory '{implementationType.FullName}' is registered with " +
                        $"both priority {existing.Priority} and {priority}.");
                }

                Registrations.Add(
                    implementationType,
                    new FactoryRegistration(implementationType, priority, factory));
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

                PlayServLocalExecutionFactoryAttribute[] attributes;
                try
                {
                    attributes = assembly
                        .GetCustomAttributes(typeof(PlayServLocalExecutionFactoryAttribute), inherit: false)
                        .OfType<PlayServLocalExecutionFactoryAttribute>()
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
                        attribute.ImplementationType,
                        attribute.Priority,
                        () => (ILocalCommandExecution)Activator.CreateInstance(
                            attribute.ImplementationType,
                            nonPublic: true));
                }
            }
        }

        private sealed class FactoryRegistration
        {
            public FactoryRegistration(
                Type implementationType,
                int priority,
                Func<ILocalCommandExecution> factory)
            {
                ImplementationType = implementationType;
                Priority = priority;
                Factory = factory;
            }

            public Type ImplementationType { get; }

            public int Priority { get; }

            public Func<ILocalCommandExecution> Factory { get; }
        }
    }
}
