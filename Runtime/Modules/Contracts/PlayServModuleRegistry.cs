using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Playserv.Modules;

namespace Playserv.Modules
{
    public static class PlayServModuleRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ModuleRegistration> Registrations =
            new Dictionary<string, ModuleRegistration>(StringComparer.Ordinal);
        private static readonly HashSet<Assembly> ScannedAssemblies = new HashSet<Assembly>();

        private static HashSet<string> _projectSelection;

        public static void Register<TModule>(string moduleId, int order)
            where TModule : IPlayServModule, new()
        {
            Register(moduleId, order, typeof(TModule), () => new TModule());
        }

        public static void RegisterDefaults(PlayServModuleHost host)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            DiscoverLoadedAssemblies();

            ModuleRegistration[] registrations;
            HashSet<string> projectSelection;
            lock (Gate)
            {
                registrations = Registrations.Values
                    .OrderBy(registration => registration.Order)
                    .ThenBy(registration => registration.ModuleId, StringComparer.Ordinal)
                    .ToArray();
                projectSelection = _projectSelection == null
                    ? null
                    : new HashSet<string>(_projectSelection, StringComparer.Ordinal);
            }

            for (var i = 0; i < registrations.Length; i++)
            {
                var registration = registrations[i];
                if (projectSelection != null && !projectSelection.Contains(registration.ModuleId))
                    continue;

                var module = registration.Factory();
                if (module == null)
                    throw new InvalidOperationException($"Module factory returned null: {registration.ModuleId}");

                host.Register(module);
            }
        }

        public static void SetProjectSelection(IEnumerable<string> enabledModuleIds)
        {
            lock (Gate)
            {
                _projectSelection = enabledModuleIds == null
                    ? null
                    : new HashSet<string>(
                        enabledModuleIds.Where(moduleId => !string.IsNullOrWhiteSpace(moduleId)),
                        StringComparer.Ordinal);
            }
        }

        public static bool IsSelected(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return false;

            lock (Gate)
                return _projectSelection == null || _projectSelection.Contains(moduleId);
        }

        public static IReadOnlyList<string> GetRegisteredModuleIds()
        {
            DiscoverLoadedAssemblies();
            lock (Gate)
            {
                return Registrations.Keys
                    .OrderBy(moduleId => moduleId, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        private static void Register(
            string moduleId,
            int order,
            Type moduleType,
            Func<IPlayServModule> factory)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                throw new ArgumentException("Module id is required.", nameof(moduleId));
            if (moduleType == null)
                throw new ArgumentNullException(nameof(moduleType));
            if (!typeof(IPlayServModule).IsAssignableFrom(moduleType))
                throw new ArgumentException($"{moduleType.FullName} does not implement {nameof(IPlayServModule)}.", nameof(moduleType));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var normalizedModuleId = moduleId.Trim();
            lock (Gate)
            {
                if (Registrations.TryGetValue(normalizedModuleId, out var existing))
                {
                    if (existing.ModuleType == moduleType)
                        return;

                    throw new InvalidOperationException(
                        $"Module '{normalizedModuleId}' is registered by both " +
                        $"{existing.ModuleType.FullName} and {moduleType.FullName}.");
                }

                Registrations.Add(
                    normalizedModuleId,
                    new ModuleRegistration(normalizedModuleId, order, moduleType, factory));
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

                PlayServModuleAttribute[] attributes;
                try
                {
                    attributes = assembly
                        .GetCustomAttributes(typeof(PlayServModuleAttribute), inherit: false)
                        .OfType<PlayServModuleAttribute>()
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
                        attribute.Order,
                        attribute.ModuleType,
                        () => (IPlayServModule)Activator.CreateInstance(attribute.ModuleType, nonPublic: true));
                }
            }
        }

        private sealed class ModuleRegistration
        {
            public ModuleRegistration(
                string moduleId,
                int order,
                Type moduleType,
                Func<IPlayServModule> factory)
            {
                ModuleId = moduleId;
                Order = order;
                ModuleType = moduleType;
                Factory = factory;
            }

            public string ModuleId { get; }

            public int Order { get; }

            public Type ModuleType { get; }

            public Func<IPlayServModule> Factory { get; }
        }
    }
}
