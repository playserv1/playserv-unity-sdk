using NUnit.Framework;
using Playserv.Modules;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServModuleRegistryTests
    {
        private const string SelectedModuleId = "test-generated-selected";
        private const string ExcludedModuleId = "test-generated-excluded";

        [SetUp]
        public void SetUp()
        {
            PlayServModuleRegistry.Register<SelectedModule>(SelectedModuleId, 10000);
            PlayServModuleRegistry.Register<ExcludedModule>(ExcludedModuleId, 10001);
        }

        [TearDown]
        public void TearDown()
        {
            PlayServModuleRegistry.SetProjectSelection(null);
        }

        [Test]
        public void ProjectSelection_FiltersRegisteredRuntimeModules()
        {
            PlayServModuleRegistry.SetProjectSelection(new[] { SelectedModuleId });
            using var host = new PlayServModuleHost();

            PlayServModuleRegistry.RegisterDefaults(host);

            Assert.That(host.HasModule(SelectedModuleId), Is.True);
            Assert.That(host.HasModule(ExcludedModuleId), Is.False);
            Assert.That(PlayServModuleRegistry.IsSelected(SelectedModuleId), Is.True);
            Assert.That(PlayServModuleRegistry.IsSelected(ExcludedModuleId), Is.False);
        }

        public sealed class SelectedModule : IPlayServModule
        {
            public PlayServModuleDescriptor Descriptor { get; } =
                new PlayServModuleDescriptor(SelectedModuleId, isCore: false);

            public void Initialize(PlayServModuleContext context)
            {
            }

            public void Shutdown()
            {
            }
        }

        public sealed class ExcludedModule : IPlayServModule
        {
            public PlayServModuleDescriptor Descriptor { get; } =
                new PlayServModuleDescriptor(ExcludedModuleId, isCore: false);

            public void Initialize(PlayServModuleContext context)
            {
            }

            public void Shutdown()
            {
            }
        }
    }
}
