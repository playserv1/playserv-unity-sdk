using NUnit.Framework;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServProductionDefaultsTests
    {
        [Test]
        public void UnqualifiedPackageDefaults_UsePublicProductionPlatform()
        {
            var settings = PlayServPackageDefaultsProvider.LoadSettingsOrDefault();
            Assert.That(settings.BackendServerAddress, Is.EqualTo("wss://dashboard.playserv.com/ws"));
            Assert.That(settings.DashboardAddress, Is.EqualTo("https://dashboard.playserv.com"));
            Assert.That(PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null),
                Is.EqualTo(settings.BackendServerAddress));
            Assert.That(PlayServPackageDefaultsProvider.ResolveDashboardAddress(null),
                Is.EqualTo(settings.DashboardAddress));
            Assert.That(settings.ClientToken, Is.Null.Or.Empty);
        }

        [TestCase("Dev", "wss://dashboard.dev.playserv.com/ws", "https://dashboard.dev.playserv.com")]
        [TestCase("Prod", "wss://dashboard.playserv.com/ws", "https://dashboard.playserv.com")]
        public void EnvironmentDefaultsAndEmptyOverrides_UseDashboardHosts(string environment, string runtime, string dashboard)
        {
            var settings = PlayServPackageDefaultsProvider.LoadSettingsOrDefault(environment);
            Assert.That(settings.BackendServerAddress, Is.EqualTo(runtime));
            Assert.That(settings.DashboardAddress, Is.EqualTo(dashboard));
            Assert.That(PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null, environment), Is.EqualTo(runtime));
            Assert.That(PlayServPackageDefaultsProvider.ResolveDashboardAddress(null, environment), Is.EqualTo(dashboard));
        }

        [TestCase("wss://platform-monolith-ag3jh77tma-ew.a.run.app/ws", "wss://dashboard.dev.playserv.com/ws")]
        [TestCase("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com/ws")]
        [TestCase("wss://proxy.playserv.io/ws", "wss://dashboard.playserv.com/ws")]
        [TestCase("wss://custom.example.test/ws", "wss://custom.example.test/ws")]
        public void ExplicitPreviousDefaults_ResolveWithoutOverwritingCustomEndpoints(string previous, string expected)
        {
            Assert.That(PlayServPackageDefaultsProvider.ResolveBackendServerAddress(previous), Is.EqualTo(expected));
            foreach (var environment in new[] { "Dev", "Prod" })
                Assert.That(PlayServPackageDefaultsProvider.ResolveBackendServerAddress(previous, environment), Is.EqualTo(expected));
        }

        [Test]
        public void ExplicitDevelopmentAndCustomEndpoints_AreUnchanged()
        {
            Assert.That(PlayServPackageDefaultsProvider.LoadSettingsOrDefault("Dev").DashboardAddress,
                Is.EqualTo("https://dashboard.dev.playserv.com"));
            Assert.That(PlayServPackageDefaultsProvider.ResolveBackendServerAddress("wss://custom.example.test/ws"),
                Is.EqualTo("wss://custom.example.test/ws"));
            Assert.That(PlayServPackageDefaultsProvider.ResolveDashboardAddress("https://custom.example.test"),
                Is.EqualTo("https://custom.example.test"));
        }
    }
}
