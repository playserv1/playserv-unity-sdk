using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Modules;

namespace Playserv.Editor
{
    internal sealed class PlayServModuleDependencyGraphResult
    {
        public PlayServModuleDependencyGraphResult(
            PlayServModuleManifestEntry[] orderedModules,
            string[] cyclePaths)
        {
            OrderedModules = orderedModules ?? Array.Empty<PlayServModuleManifestEntry>();
            CyclePaths = cyclePaths ?? Array.Empty<string>();
        }

        public PlayServModuleManifestEntry[] OrderedModules { get; }

        public string[] CyclePaths { get; }
    }

    internal static class PlayServModuleDependencyGraph
    {
        public static PlayServModuleDependencyGraphResult Order(
            IEnumerable<PlayServModuleManifestEntry> modules)
        {
            var moduleArray = (modules ?? Array.Empty<PlayServModuleManifestEntry>())
                .Where(module => module != null && !string.IsNullOrWhiteSpace(module.Id))
                .GroupBy(module => module.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var modulesById = moduleArray.ToDictionary(module => module.Id, StringComparer.Ordinal);
            var dependencyCounts = moduleArray.ToDictionary(module => module.Id, _ => 0, StringComparer.Ordinal);
            var dependents = moduleArray.ToDictionary(
                module => module.Id,
                _ => new List<PlayServModuleManifestEntry>(),
                StringComparer.Ordinal);

            for (var i = 0; i < moduleArray.Length; i++)
            {
                var module = moduleArray[i];
                foreach (var dependencyId in GetKnownDependencyIds(module, modulesById))
                {
                    dependencyCounts[module.Id]++;
                    dependents[dependencyId].Add(module);
                }
            }

            var ready = moduleArray
                .Where(module => dependencyCounts[module.Id] == 0)
                .ToList();
            SortModules(ready);

            var ordered = new List<PlayServModuleManifestEntry>(moduleArray.Length);
            while (ready.Count > 0)
            {
                var module = ready[0];
                ready.RemoveAt(0);
                ordered.Add(module);

                var moduleDependents = dependents[module.Id];
                SortModules(moduleDependents);
                for (var i = 0; i < moduleDependents.Count; i++)
                {
                    var dependent = moduleDependents[i];
                    dependencyCounts[dependent.Id]--;
                    if (dependencyCounts[dependent.Id] == 0)
                        ready.Add(dependent);
                }

                SortModules(ready);
            }

            var unresolved = moduleArray
                .Where(module => dependencyCounts[module.Id] > 0)
                .ToArray();
            var cyclePaths = FindCyclePaths(unresolved, modulesById);

            if (unresolved.Length > 0)
            {
                var unresolvedIds = new HashSet<string>(
                    unresolved.Select(module => module.Id),
                    StringComparer.Ordinal);
                var blocked = unresolved
                    .Where(module => !ordered.Any(item => string.Equals(item.Id, module.Id, StringComparison.Ordinal)))
                    .ToList();
                SortModules(blocked);
                ordered.AddRange(blocked.Where(module => unresolvedIds.Contains(module.Id)));
            }

            return new PlayServModuleDependencyGraphResult(ordered.ToArray(), cyclePaths);
        }

        private static string[] FindCyclePaths(
            IReadOnlyCollection<PlayServModuleManifestEntry> unresolved,
            IReadOnlyDictionary<string, PlayServModuleManifestEntry> modulesById)
        {
            if (unresolved.Count == 0)
                return Array.Empty<string>();

            var unresolvedIds = new HashSet<string>(
                unresolved.Select(module => module.Id),
                StringComparer.Ordinal);
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            var stack = new List<string>();
            var cyclePaths = new HashSet<string>(StringComparer.Ordinal);
            var ordered = unresolved.ToList();
            SortModules(ordered);

            for (var i = 0; i < ordered.Count; i++)
            {
                if (!states.TryGetValue(ordered[i].Id, out var state) || state == 0)
                {
                    Visit(
                        ordered[i],
                        modulesById,
                        unresolvedIds,
                        states,
                        stack,
                        cyclePaths);
                }
            }

            return cyclePaths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static void Visit(
            PlayServModuleManifestEntry module,
            IReadOnlyDictionary<string, PlayServModuleManifestEntry> modulesById,
            ISet<string> unresolvedIds,
            IDictionary<string, int> states,
            IList<string> stack,
            ISet<string> cyclePaths)
        {
            states[module.Id] = 1;
            stack.Add(module.Id);

            var dependencies = GetKnownDependencyIds(module, modulesById)
                .Where(unresolvedIds.Contains)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            for (var i = 0; i < dependencies.Length; i++)
            {
                var dependencyId = dependencies[i];
                states.TryGetValue(dependencyId, out var dependencyState);
                if (dependencyState == 0)
                {
                    Visit(
                        modulesById[dependencyId],
                        modulesById,
                        unresolvedIds,
                        states,
                        stack,
                        cyclePaths);
                    continue;
                }

                if (dependencyState != 1)
                    continue;

                var cycleStart = stack.IndexOf(dependencyId);
                if (cycleStart < 0)
                    continue;

                var cycle = stack.Skip(cycleStart).Concat(new[] { dependencyId });
                cyclePaths.Add(string.Join(" -> ", cycle));
            }

            stack.RemoveAt(stack.Count - 1);
            states[module.Id] = 2;
        }

        private static IEnumerable<string> GetKnownDependencyIds(
            PlayServModuleManifestEntry module,
            IReadOnlyDictionary<string, PlayServModuleManifestEntry> modulesById)
        {
            return module.DependencyIds
                .Concat(module.HiddenDependencyModuleIds)
                .Where(modulesById.ContainsKey)
                .Distinct(StringComparer.Ordinal);
        }

        private static void SortModules(List<PlayServModuleManifestEntry> modules)
        {
            modules.Sort((left, right) =>
            {
                var order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
            });
        }
    }
}
