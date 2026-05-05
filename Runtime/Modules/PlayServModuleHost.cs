using System;
using System.Collections.Generic;

namespace Playserv.Modules
{
    public sealed class PlayServModuleHost : IDisposable
    {
        private readonly Dictionary<string, IPlayServModule> _modules = new Dictionary<string, IPlayServModule>(StringComparer.Ordinal);
        private readonly List<IPlayServModule> _initializedModules = new List<IPlayServModule>();
        private readonly PlayServModuleServiceRegistry _services = new PlayServModuleServiceRegistry();
        private bool _initialized;

        public IPlayServModuleServiceProvider Services => _services;

        public IPlayServModuleServiceRegistry ServiceRegistry => _services;

        public void Register(IPlayServModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            var descriptor = module.Descriptor ?? throw new InvalidOperationException("Module descriptor is required.");
            if (_initialized)
                throw new InvalidOperationException("Cannot register modules after host initialization.");

            if (_modules.ContainsKey(descriptor.Id))
                throw new InvalidOperationException($"Module already registered: {descriptor.Id}");

            _modules.Add(descriptor.Id, module);
        }

        public bool HasModule(string moduleId)
        {
            return !string.IsNullOrWhiteSpace(moduleId) && _modules.ContainsKey(moduleId);
        }

        public void Initialize(PlayServModuleContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (_initialized)
                return;

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (var moduleId in _modules.Keys)
                InitializeModule(moduleId, context, visiting, visited);

            _initialized = true;
        }

        public void NotifyConnected()
        {
            for (var i = 0; i < _initializedModules.Count; i++)
            {
                if (_initializedModules[i] is IPlayServConnectionAwareModule connectionAwareModule)
                    connectionAwareModule.OnConnected();
            }
        }

        public void Dispose()
        {
            for (var i = _initializedModules.Count - 1; i >= 0; i--)
                _initializedModules[i].Shutdown();

            _initializedModules.Clear();
            _initialized = false;
        }

        private void InitializeModule(
            string moduleId,
            PlayServModuleContext context,
            HashSet<string> visiting,
            HashSet<string> visited)
        {
            if (visited.Contains(moduleId))
                return;

            if (!_modules.TryGetValue(moduleId, out var module))
                throw new InvalidOperationException($"Module dependency is not registered: {moduleId}");

            if (!visiting.Add(moduleId))
                throw new InvalidOperationException($"Circular module dependency detected at module: {moduleId}");

            var dependencies = module.Descriptor.Dependencies;
            for (var i = 0; i < dependencies.Count; i++)
                InitializeModule(dependencies[i], context, visiting, visited);

            visiting.Remove(moduleId);
            module.Initialize(context);
            _initializedModules.Add(module);
            visited.Add(moduleId);
        }
    }
}
