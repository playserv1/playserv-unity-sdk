using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Playserv.Editor;
using UnityEditor;
using UnityEngine.TestTools;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSdkUpdateIntegrationTests
    {
        [UnityTest]
        public IEnumerator AutomaticDependencyInstall_WaitsForGateWithoutConsumingAttempt()
        {
            const string key = "PlayServ.NewtonsoftJsonDependency.AutoInstallAttempted";
            var original = SessionState.GetBool(key, false);
            var owner = new object();
            SessionState.SetBool(key, false);
            Assert.That(PlayServPackageOperationGate.Shared.TryAcquire(owner), Is.True);
            try
            {
                PlayServPackageDependencyInstaller.QueueAutomaticInstall();
                Assert.That(SessionState.GetBool(key, false), Is.False, "Waiting for another operation is not an install attempt.");
                PlayServPackageOperationGate.Shared.Release(owner);
                PlayServPackageDependencyInstaller.TryAutomaticInstall();
                var deadline = EditorApplication.timeSinceStartup + 30;
                Assert.That(SessionState.GetBool(key, false), Is.True);
                var field = typeof(PlayServPackageDependencyInstaller).GetField("_listRequest", BindingFlags.Static | BindingFlags.NonPublic);
                var request = field.GetValue(null);
                Assert.That(request, Is.Not.Null);
                PlayServPackageDependencyInstaller.QueueAutomaticInstall();
                Assert.That(field.GetValue(null), Is.SameAs(request), "Only one initial request is submitted.");
                while (PlayServPackageOperationGate.Shared.IsBusy && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(PlayServPackageOperationGate.Shared.IsBusy, Is.False);
            }
            finally
            {
                PlayServPackageOperationGate.Shared.Release(owner);
                SessionState.SetBool(key, original);
            }
        }

        [Test]
        public void DependencyInstaller_RespectsSharedPackageOperationGate()
        {
            var owner = new object();
            Assert.That(PlayServPackageOperationGate.Shared.TryAcquire(owner), Is.True);
            try
            {
                PlayServPackageDependencyInstaller.InstallNewtonsoftJson();
                var field = typeof(PlayServPackageDependencyInstaller).GetField("_listRequest", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(field.GetValue(null), Is.Null, "A concurrent PlayServ UPM request must not start.");
            }
            finally { PlayServPackageOperationGate.Shared.Release(owner); }
        }

        [Test]
        public void Store_IsolatesProjectsAndRecoversJournalAcrossInstances()
        {
            var root = Path.Combine(Path.GetTempPath(), "playserv-updater-test-" + Guid.NewGuid().ToString("N"));
            var first = new PlayServSdkUpdateStore(Path.Combine(root, "first"));
            var second = new PlayServSdkUpdateStore(Path.Combine(root, "second"));
            try
            {
                first.Cache = "first-check";
                first.Pending = "first-plan";
                Assert.That(second.Cache, Is.Empty);
                Assert.That(second.Pending, Is.Null);
                var reloaded = new PlayServSdkUpdateStore(Path.Combine(root, "first"));
                Assert.That(reloaded.Cache, Is.EqualTo("first-check"));
                Assert.That(reloaded.Pending, Is.EqualTo("first-plan"));
                reloaded.Pending = "replacement-plan";
                Assert.That(first.Pending, Is.EqualTo("replacement-plan"));
                reloaded.Pending = null;
                Assert.That(first.Pending, Is.Null);
            }
            finally
            {
                first.Cache = null;
                second.Cache = null;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestCase("2021.3", null, "2021.3.45f2", true)]
        [TestCase("2021.3", "45f2", "2021.3.45f1", false)]
        [TestCase("2021.3", "45f2", "2021.3.45f2", true)]
        [TestCase("6000.6", null, "2021.3.45f2", false)]
        [TestCase("6000.6", "0f1", "6000.6.0b9", false)]
        [TestCase("invalid", null, "6000.6.0f1", false)]
        public void GitManifest_RespectsUnityCompatibilityAndDependencies(string unity, string release, string editor, bool compatible)
        {
            var json = "{\"name\":\"com.playserv.schema-tool\",\"version\":\"0.6.2\",\"unity\":\"" + unity + "\"," +
                (release == null ? "" : "\"unityRelease\":\"" + release + "\",") +
                "\"dependencies\":{\"com.playserv.sdk\":\"0.6.2\"}}";
            var parsed = PlayServSdkGitRelease.Parse(json, editor);
            Assert.That(parsed.CompatibleVersions.Length, Is.EqualTo(compatible ? 1 : 0));
            Assert.That(parsed.DependencyNames, Is.EqualTo(new[] { "com.playserv.sdk" }));
        }

        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase("{\"name\":\"p\",\"version\":\"1.0.0\",\"dependencies\":[]}")]
        [TestCase("{\"name\":\"p\",\"version\":\"1.0.0\",\"unity\":2021}")]
        public void GitManifest_RejectsMalformedMetadata(string json)
        {
            Assert.Throws<InvalidOperationException>(() => PlayServSdkGitRelease.Parse(json, "2021.3.45f2"));
        }
    }
}
