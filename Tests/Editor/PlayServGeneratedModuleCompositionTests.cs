using System;
using System.Linq;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Modules;
using UnityEngine;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServGeneratedModuleCompositionTests
    {
        [Serializable]
        private sealed class AssemblyDefinitionModel
        {
            public string name;
            public string[] references;
        }

        [SetUp]
        public void SetUp()
        {
            PlayServModuleManifestJsonRegistry.Reload();
        }

        [Test]
        public void GenerateProjectAssemblyDefinition_ReferencesOnlyModuleContracts()
        {
            var json = PlayServGeneratedCompatibilityLayer.GenerateProjectAssemblyDefinition();
            var model = JsonUtility.FromJson<AssemblyDefinitionModel>(json);

            Assert.That(model.name, Is.EqualTo("Playserv.Project.Generated"));
            Assert.That(model.references, Is.EqualTo(new[] { "Playserv.Runtime.Modules" }));
        }

        [Test]
        public void GenerateProjectModuleSelection_ContainsOnlyEnabledModulesInStableOrder()
        {
            var state = new PlayServRuntimeModuleState();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                state.SetEnabled(module.Id, false);

            state.SetEnabled(PlayServModuleManifest.ClientExecutionId, true);
            state.SetEnabled(PlayServModuleManifest.EventsId, true);
            state.SetEnabled(PlayServModuleManifest.DataSubscriptionId, true);
            PlayServRuntimeModuleDefines.NormalizeDependencies(state);

            var source = PlayServGeneratedCompatibilityLayer.GenerateProjectModuleSelection(state);
            var expectedIds = new[]
            {
                PlayServModuleManifest.ClientExecutionId,
                PlayServModuleManifest.EventsId,
                PlayServModuleManifest.DataSubscriptionId
            };

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                var expected = expectedIds.Contains(module.Id, StringComparer.Ordinal);
                Assert.That(
                    source.Contains("\"" + module.Id + "\""),
                    Is.EqualTo(expected),
                    module.Id);
            }

            Assert.That(source.IndexOf("\"" + expectedIds[0] + "\"", StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("\"" + expectedIds[1] + "\"", StringComparison.Ordinal)));
            Assert.That(source.IndexOf("\"" + expectedIds[1] + "\"", StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("\"" + expectedIds[2] + "\"", StringComparison.Ordinal)));
            StringAssert.Contains("PlayServModuleRegistry.SetProjectSelection(EnabledIds);", source);
        }
    }
}
