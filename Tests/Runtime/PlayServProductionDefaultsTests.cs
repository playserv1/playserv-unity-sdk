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
            Assert.That(settings.BackendServerAddress, Is.EqualTo("wss://platform.playserv.com/ws"));
            Assert.That(settings.DashboardAddress, Is.EqualTo("https://dashboard.playserv.com"));
            Assert.That(PlayServPackageDefaultsProvider.ResolveBackendServerAddress(null),
                Is.EqualTo(settings.BackendServerAddress));
            Assert.That(PlayServPackageDefaultsProvider.ResolveDashboardAddress(null),
                Is.EqualTo(settings.DashboardAddress));
            Assert.That(settings.ClientToken, Is.Null.Or.Empty);
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
