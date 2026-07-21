using System;
using System.Collections.Generic;
using System.Linq;
using Playserv.Modules;
using UnityEditor;

namespace Playserv.Editor
{
    internal static class PlayServModuleCodegenRegistry
    {
        public static PlayServModuleCodegenContext Build(PlayServRuntimeModuleState runtimeState)
        {
            var normalizedState = runtimeState?.Clone() ?? new PlayServRuntimeModuleState();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(normalizedState);
            PlayServRuntimeModuleDefines.NormalizeDependencies(normalizedState);

            var context = new PlayServModuleCodegenContext(normalizedState);
            var contributors = DiscoverContributors();
            var contributedModuleIds = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < contributors.Count; i++)
            {
                var contributor = contributors[i];
                if (!PlayServModuleManifest.TryGet(contributor.ModuleId, out var module))
                    throw new InvalidOperationException($"Module codegen contributor references unknown module id: {contributor.ModuleId}");

                if (!contributedModuleIds.Add(module.Id))
                    throw new InvalidOperationException($"Multiple module codegen contributors are registered for module: {module.Id}");

                if (!normalizedState.IsEnabled(module.Id) || !IsReadyForGeneration(module))
                    continue;

                context.BeginContribution(module.Id);
                try
                {
                    contributor.Contribute(context);
                }
                finally
                {
                    context.EndContribution();
                }
            }

            return context;
        }

        internal static IReadOnlyList<IPlayServModuleCodegenContributor> DiscoverContributors()
        {
            var contributors = new List<IPlayServModuleCodegenContributor>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IPlayServModuleCodegenContributor>())
            {
                if (type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
                    continue;

                try
                {
                    contributors.Add((IPlayServModuleCodegenContributor)Activator.CreateInstance(type, nonPublic: true));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to create module codegen contributor: {type.FullName}",
                        ex.GetBaseException());
                }
            }

            return contributors
                .OrderBy(contributor => contributor.Order)
                .ThenBy(contributor => contributor.ModuleId, StringComparer.Ordinal)
                .ThenBy(contributor => contributor.GetType().FullName, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsReadyForGeneration(PlayServModuleManifestEntry module)
        {
            return string.IsNullOrWhiteSpace(module.RootAssemblyReference) ||
                   PlayServCoreAssemblyReferenceSync.HasRuntimeModuleReference(module.Id);
        }
    }
}
