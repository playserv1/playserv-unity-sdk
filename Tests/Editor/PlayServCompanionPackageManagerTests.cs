using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Playserv.Editor;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServCompanionPackageManagerTests
    {
        [Test]
        public void Catalog_UsesUniquePackageAndModuleIds()
        {
            var packages = PlayServCompanionPackageCatalog.All;
            var moduleIds = packages
                .SelectMany(package => package.ModuleIds)
                .ToArray();

            Assert.That(packages.Count, Is.EqualTo(6));
            Assert.That(
                packages.Select(package => package.PackageId).Distinct().Count(),
                Is.EqualTo(packages.Count));
            Assert.That(
                moduleIds.Distinct().Count(),
                Is.EqualTo(moduleIds.Length));
        }

        [TestCase("com.playserv.apple-signin", "apple-sign-in")]
        [TestCase("com.playserv.google-signin", "google-sign-in")]
        [TestCase("com.playserv.webrtc", "transport-webrtc")]
        [TestCase("com.playserv.analytics", "analytics")]
        [TestCase("com.playserv.pulse", "pulse")]
        [TestCase("com.playserv.transports-native", "transport-udp")]
        [TestCase("com.playserv.transports-native", "transport-rudp")]
        public void GeneratedCatalog_MapsPackageAndModuleIds(
            string packageId,
            string moduleId)
        {
            Assert.That(
                PlayServCompanionPackageCatalog.TryGetByPackageId(
                    packageId,
                    out var package),
                Is.True);
            Assert.That(package.ModuleIds, Contains.Item(moduleId));
            Assert.That(
                PlayServCompanionPackageCatalog.TryGetByModuleId(
                    moduleId,
                    out var modulePackage),
                Is.True);
            Assert.That(modulePackage, Is.SameAs(package));
            Assert.That(
                PlayServCompanionPackageCatalog.IsCompanionModule(moduleId),
                Is.True);
        }

        [Test]
        public void BuildGitReference_UsesCompanionPathAndExactRevision()
        {
            var package = PlayServCompanionPackageCatalog.All[0];

            var result = PlayServCompanionPackageManager.BuildGitReference(
                "git@github.com:playserv1/playserv-unity-sdk.git?path=/old#old-ref",
                package.GitPath,
                "abc123");

            Assert.That(
                result,
                Is.EqualTo(
                    "git@github.com:playserv1/playserv-unity-sdk.git" +
                    "?path=/CompanionPackages~/com.playserv.apple-signin#abc123"));
        }

        [Test]
        public void BuildRegistryReference_UsesExactCoreVersion()
        {
            Assert.That(
                PlayServCompanionPackageCatalog.TryGetByPackageId(
                    "com.playserv.webrtc",
                    out var package),
                Is.True);

            var result = PlayServCompanionPackageManager.BuildRegistryReference(
                package.PackageId,
                "0.3.1");

            Assert.That(result, Is.EqualTo("com.playserv.webrtc@0.3.1"));
        }

        [Test]
        public void ModuleLifecycle_RecognizesInstalledCompanionPackage()
        {
            const string packageId = "com.playserv.apple-signin";
            if (!PlayServCompanionPackageManager.TryGetInstalledPackage(
                    packageId,
                    out var packageInfo))
            {
                Assert.Ignore($"{packageId} is not installed in this test project.");
            }

            Assert.That(packageInfo.isDirectDependency, Is.True);
            PlayServModuleManifestJsonRegistry.Reload();
            var module = PlayServEditorModuleAvailability.AvailableRuntimeModules
                .SingleOrDefault(candidate =>
                    string.Equals(
                        candidate.Id,
                        "apple-sign-in",
                        StringComparison.Ordinal));
            Assert.That(module, Is.Not.Null);

            var settings = new PlayServEditorModuleSettings();
            settings.Load();

            Assert.That(
                PlayServRuntimeModuleLifecycle.CanUninstallModule(
                    settings,
                    module,
                    out var reason),
                Is.True,
                reason);
        }

        [Test]
        public void TryBuildLocalReference_UsesPackageInsideCoreCheckout()
        {
            var package = PlayServCompanionPackageCatalog.All[1];
            var root = Path.Combine(
                Path.GetTempPath(),
                "PlayServCompanionPackageManagerTests",
                Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(root, package.GitPath);

            try
            {
                Directory.CreateDirectory(packageRoot);
                File.WriteAllText(Path.Combine(packageRoot, "package.json"), "{}");

                var success = PlayServCompanionPackageManager.TryBuildLocalReference(
                    package,
                    root,
                    out var installReference,
                    out var error);

                Assert.That(success, Is.True, error);
                Assert.That(
                    installReference,
                    Is.EqualTo(new Uri(packageRoot).AbsoluteUri));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Validator_IgnoresCompanionSourcesHiddenInsideCorePackage()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "playserv-core-package");
            var hiddenAsmdef = Path.Combine(
                root,
                "CompanionPackages~",
                "com.playserv.analytics",
                "Runtime",
                "Playserv.Runtime.Modules.Analytics.asmdef");
            var runtimeAsmdef = Path.Combine(
                root,
                "Runtime",
                "Playserv.Runtime.asmdef");

            Assert.That(
                PlayServModuleValidator.IsHiddenUpmSourcePath(root, hiddenAsmdef),
                Is.True);
            Assert.That(
                PlayServModuleValidator.IsHiddenUpmSourcePath(root, runtimeAsmdef),
                Is.False);
        }
    }
}
