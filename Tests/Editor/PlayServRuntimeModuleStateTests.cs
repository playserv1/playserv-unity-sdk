using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Modules;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServRuntimeModuleStateTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayServModuleManifestJsonRegistry.Reload();
        }

        [Test]
        public void NormalizeDependencies_AllModulesEnabled_PreservesEveryModule()
        {
            var state = CreateState(enabled: true);

            PlayServRuntimeModuleDefines.NormalizeDependencies(state);

            Assert.That(
                PlayServModuleManifest.RuntimeModules
                    .Where(module => !state.IsEnabled(module.Id))
                    .Select(module => module.Id),
                Is.Empty);
        }

        [Test]
        public void NormalizeDependencies_AllModulesDisabled_DisablesHiddenDependencies()
        {
            var state = CreateState(enabled: false);

            PlayServRuntimeModuleDefines.NormalizeDependencies(state);

            Assert.That(
                PlayServModuleManifest.RuntimeModules
                    .Where(module => state.IsEnabled(module.Id))
                    .Select(module => module.Id),
                Is.Empty);
        }

        [Test]
        public void NormalizeDependencies_MissingRequiredDependency_DisablesDependentModule()
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                foreach (var dependencyId in module.DependencyIds)
                {
                    var state = CreateState(enabled: false);
                    state.SetEnabled(module.Id, true);
                    state.SetEnabled(dependencyId, false);

                    PlayServRuntimeModuleDefines.NormalizeDependencies(state);

                    Assert.That(
                        state.IsEnabled(module.Id),
                        Is.False,
                        $"{module.Id} stayed enabled without {dependencyId}.");
                }
            }
        }

        [Test]
        public void NormalizeDependencies_HiddenDependency_IsEnabledAndReleasedAutomatically()
        {
            var modulesWithHiddenDependencies = PlayServModuleManifest.RuntimeModules
                .Where(module => module.HiddenDependencyModuleIds.Length > 0)
                .ToArray();
            Assert.That(modulesWithHiddenDependencies, Is.Not.Empty);

            foreach (var module in modulesWithHiddenDependencies)
            {
                var state = CreateState(enabled: false);
                foreach (var dependencyId in module.DependencyIds)
                    state.SetEnabled(dependencyId, true);
                state.SetEnabled(module.Id, true);

                PlayServRuntimeModuleDefines.NormalizeDependencies(state);

                foreach (var dependencyId in module.HiddenDependencyModuleIds)
                    Assert.That(state.IsEnabled(dependencyId), Is.True, $"{dependencyId} was not enabled.");

                state.SetEnabled(module.Id, false);
                PlayServRuntimeModuleDefines.NormalizeDependencies(state);

                foreach (var dependencyId in module.HiddenDependencyModuleIds)
                    Assert.That(state.IsEnabled(dependencyId), Is.False, $"{dependencyId} stayed enabled.");
            }
        }

        [Test]
        public void DisableDefines_ControlEveryDiscoveredModule()
        {
            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                var defines = new HashSet<string>(StringComparer.Ordinal);
                Assert.That(PlayServRuntimeModuleDefines.IsModuleEnabled(defines, module.Id), Is.True);

                defines.Add(module.DisableDefine);
                Assert.That(
                    PlayServRuntimeModuleDefines.IsModuleEnabled(defines, module.Id),
                    Is.False,
                    module.Id);
            }
        }

        private static PlayServRuntimeModuleState CreateState(bool enabled)
        {
            var state = new PlayServRuntimeModuleState();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                state.SetEnabled(module.Id, enabled);

            return state;
        }
    }
}
