using System;
using System.IO;
using NUnit.Framework;
using Playserv.Editor;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSdkCacheMaintenanceTests
    {
        [Test]
        public void IsStateCurrent_RequiresMatchingSchemaAndSdkVersion()
        {
            var state = new PlayServSdkCacheState
            {
                schemaVersion = PlayServSdkCacheMaintenance.CurrentCacheSchemaVersion,
                sdkVersion = "0.4.0"
            };

            Assert.That(
                PlayServSdkCacheMaintenance.IsStateCurrent(state, "0.4.0"),
                Is.True);
            Assert.That(
                PlayServSdkCacheMaintenance.IsStateCurrent(state, "0.4.1"),
                Is.False);

            state.schemaVersion--;
            Assert.That(
                PlayServSdkCacheMaintenance.IsStateCurrent(state, "0.4.0"),
                Is.False);
        }

        [Test]
        public void FindStalePackageCaches_PreservesInstalledAndUnrelatedDirectories()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "PlayServSdkCacheMaintenanceTests",
                Guid.NewGuid().ToString("N"));
            var currentCore = Path.Combine(root, "com.playserv.sdk@current");
            var staleCore = Path.Combine(root, "com.playserv.sdk@old");
            var staleGoogle = Path.Combine(root, "com.playserv.google-signin@old");
            var unrelated = Path.Combine(root, "com.example.package@old");

            try
            {
                Directory.CreateDirectory(currentCore);
                Directory.CreateDirectory(staleCore);
                Directory.CreateDirectory(staleGoogle);
                Directory.CreateDirectory(unrelated);

                var stale = PlayServSdkCacheMaintenance.FindStalePackageCacheDirectories(
                    root,
                    new[] { currentCore });

                CollectionAssert.AreEquivalent(
                    new[] { staleCore, staleGoogle },
                    stale);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void ShouldSkipAutomaticMaintenance_SkipsOnlyBatchTestRuns()
        {
            Assert.That(
                PlayServSdkCacheMaintenance.ShouldSkipAutomaticMaintenance(
                    true,
                    new[] { "-batchmode", "-runTests", "-testPlatform", "editmode" }),
                Is.True);
            Assert.That(
                PlayServSdkCacheMaintenance.ShouldSkipAutomaticMaintenance(
                    true,
                    new[] { "-batchmode", "-RUNTESTS" }),
                Is.True);
            Assert.That(
                PlayServSdkCacheMaintenance.ShouldSkipAutomaticMaintenance(
                    true,
                    new[] { "-batchmode", "-executeMethod", "Build.Run" }),
                Is.False);
            Assert.That(
                PlayServSdkCacheMaintenance.ShouldSkipAutomaticMaintenance(
                    false,
                    new[] { "-runTests" }),
                Is.False);
        }
    }
}
