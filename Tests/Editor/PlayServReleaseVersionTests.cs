using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;
using UnityEditor.PackageManager;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServReleaseVersionTests
    {
        private const string ExpectedVersion = "0.4.0";

        [Test]
        public void CoreAndEveryCompanion_ReportReleaseVersion()
        {
            Assert.That(SdkInfo.Version, Is.EqualTo(ExpectedVersion));
            Assert.That(new PlayServRuntimeSettings().SdkVersion, Is.EqualTo(ExpectedVersion));

            var package = PackageInfo.FindForAssetPath("Packages/com.playserv.sdk/package.json");
            Assert.That(package, Is.Not.Null);
            AssertPackageVersion(Path.Combine(package.resolvedPath, "package.json"), requireCoreDependency: false);

            var companionRoot = Path.Combine(package.resolvedPath, "CompanionPackages~");
            var manifests = Directory.GetFiles(companionRoot, "package.json", SearchOption.AllDirectories)
                .OrderBy(path => path)
                .ToArray();
            Assert.That(manifests.Length, Is.EqualTo(12));
            foreach (var manifest in manifests)
                AssertPackageVersion(manifest, requireCoreDependency: true);

            var debugRoot = Path.Combine(companionRoot, "com.playserv.debug-terminal");
            StringAssert.Contains(
                $"\"com.playserv.analytics\": \"{ExpectedVersion}\"",
                File.ReadAllText(Path.Combine(debugRoot, "package.json")));
            StringAssert.Contains(
                $"\"minSdkVersion\": \"{ExpectedVersion}\"",
                File.ReadAllText(Path.Combine(debugRoot, "module.playserv.json")));

            var gameServerRoot = Path.Combine(companionRoot, "com.playserv.game-server");
            StringAssert.Contains(
                "\"minSdkVersion\": \"0.3.9\"",
                File.ReadAllText(Path.Combine(gameServerRoot, "module.playserv.json")));
        }

        private static void AssertPackageVersion(string manifest, bool requireCoreDependency)
        {
            var contents = File.ReadAllText(manifest);
            StringAssert.Contains($"\"version\": \"{ExpectedVersion}\"", contents, manifest);
            if (requireCoreDependency)
            {
                StringAssert.Contains(
                    $"\"com.playserv.sdk\": \"{ExpectedVersion}\"",
                    contents,
                    manifest);
            }

            foreach (Match dependency in Regex.Matches(
                         contents,
                         "\"(?<name>com\\.playserv\\.[^\"]+)\"\\s*:\\s*\"(?<version>[^\"]+)\""))
            {
                Assert.That(
                    dependency.Groups["version"].Value,
                    Is.EqualTo(ExpectedVersion),
                    $"{manifest}: {dependency.Groups["name"].Value}");
            }
        }
    }
}
