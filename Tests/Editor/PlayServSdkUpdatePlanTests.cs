using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Playserv.Editor;
using UnityEditor.PackageManager;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSdkUpdatePlanTests
    {
        private const string Core = "com.playserv.sdk";
        private const string Schema = "com.playserv.schema-tool";
        private const string GitUrl = "https://github.com/playserv1/playserv-unity-sdk.git";

        [TestCase("0.6.1", "0.6.2,0.10.0,0.9.0", "0.10.0")]
        [TestCase("0.6.1", "0.6.1,0.6.0", null)]
        [TestCase("0.6.1", "0.7.0-preview.1,0.6.2", "0.6.2")]
        [TestCase("0.6.1", "0.6.2+build,01.2.3,1.2,broken", null)]
        [TestCase("0.6.1-preview.1", "0.6.2", null)]
        public void Latest_UsesOnlyNewerStableCompatibleVersions(string installed, string versions, string expected)
        {
            Assert.That(Call("SelectLatest", installed, versions.Split(',')), Is.EqualTo(expected));
        }

        [Test]
        public void RegistryPlan_UpdatesOnlyInstalledPackagesIncludingSchemaTool()
        {
            var plan = Create("0.6.2", Package(Core), Package(Schema), Package("com.other.plugin"));
            CollectionAssert.AreEquivalent(new[] { Core + "@0.6.2", Schema + "@0.6.2" }, References(plan));
        }

        [TestCase("https://github.com/playserv1/playserv-unity-sdk.git")]
        [TestCase("git@github.com:playserv1/playserv-unity-sdk.git")]
        [TestCase("ssh://git@github.com/playserv1/playserv-unity-sdk.git")]
        public void GitCompanion_PreservesRepositoryAndUsesExactReleaseTag(string url)
        {
            var plan = Create("0.6.2", Package(Core), Package(Schema, source: PackageSource.Git,
                reference: url + "?path=/CompanionPackages~/" + Schema + "#0.6.1"));
            Assert.That(References(plan), Contains.Item(url + "?path=/CompanionPackages~/" + Schema + "#0.6.2"));
        }

        [TestCase(PackageSource.Git)]
        [TestCase(PackageSource.Local)]
        [TestCase(PackageSource.Embedded)]
        public void NonRegistryCore_IsNotConverted(PackageSource source)
        {
            Rejected("registry", () => Create("0.6.2", Package(Core, source: source)));
        }

        [TestCase(PackageSource.Local)]
        [TestCase(PackageSource.Embedded)]
        public void LocalCompanion_BlocksWholeUpdate(PackageSource source)
        {
            Rejected(Schema, () => Create("0.6.2", Package(Core), Package(Schema, source: source)));
        }

        [TestCase("https://github.com/fork/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#0.6.1")]
        [TestCase("https://github.com/playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#main")]
        [TestCase("https://github.com/playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#abc123")]
        [TestCase("https://github.com/playserv1/playserv-unity-sdk.git?path=/other#0.6.1")]
        [TestCase("https://github.com/playserv1/playserv-unity-sdk.git.evil?path=/CompanionPackages~/com.playserv.schema-tool#0.6.1")]
        [TestCase("https://secret@github.com/playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#0.6.1")]
        public void NonReleaseGitCompanion_IsNotSilentlyReplaced(string reference)
        {
            var error = Rejected(Schema, () => Create("0.6.2", Package(Core),
                Package(Schema, source: PackageSource.Git, reference: reference)));
            Assert.That(error.Message, Does.Not.Contain(reference));
            Assert.That(error.Message, Does.Not.Contain("secret"));
        }

        [Test]
        public void CompanionNewerThanTarget_BlocksDowngrade()
        {
            Rejected("downgrade", () => Create("0.6.2", Package(Core), Package(Schema, "0.7.0")));
        }

        [Test]
        public void IndirectDependencyRequiringChange_IsNotPromotedToDirect()
        {
            Rejected("dependency", () => Create("0.6.2", Package(Core), Package(Schema, direct: false)));
        }

        [Test]
        public void IndirectAlreadyAlignedPackage_IsNotAdded()
        {
            var plan = Create("0.6.2", Package(Core), Package(Schema, "0.6.2", direct: false));
            CollectionAssert.AreEqual(new[] { Core + "@0.6.2" }, References(plan));
        }

        [Test]
        public void DowngradeOrSameCoreVersion_IsRejected()
        {
            Rejected("newer", () => Create("0.6.1", Package(Core)));
            Rejected("newer", () => Create("0.6.0", Package(Core)));
        }

        private static object Package(string id, string version = "0.6.1", PackageSource source = PackageSource.Registry,
            bool direct = true, string reference = null)
        {
            var type = Find("PlayServPackageSnapshot");
            var value = Activator.CreateInstance(type);
            Set(value, "Name", id);
            Set(value, "Version", version);
            Set(value, "Source", source);
            Set(value, "IsDirect", direct);
            Set(value, "PackageId", id + "@" + (reference ?? version));
            Set(value, "RegistryUrl", "https://package.openupm.com");
            return value;
        }

        private static object Create(string target, params object[] packages)
        {
            var array = Array.CreateInstance(Find("PlayServPackageSnapshot"), packages.Length);
            for (var i = 0; i < packages.Length; i++) array.SetValue(packages[i], i);
            return Call("Create", array, target);
        }

        private static object Call(string name, params object[] args)
        {
            var method = Find("PlayServSdkUpdatePlan").GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "The SDK update planner must expose " + name);
            return method.Invoke(null, args);
        }

        private static Type Find(string name)
        {
            var type = typeof(PlayServWindow).Assembly.GetType("Playserv.Editor." + name);
            Assert.That(type, Is.Not.Null, "The SDK cannot plan safe updates yet: " + name);
            return type;
        }

        private static void Set(object value, string field, object content) => value.GetType().GetField(field).SetValue(value, content);
        private static string[] References(object plan) => (string[])plan.GetType().GetProperty("References").GetValue(plan);

        private static Exception Rejected(string message, TestDelegate action)
        {
            var error = Assert.Throws<TargetInvocationException>(action).InnerException;
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(error.Message, Does.Contain(message));
            return error;
        }
    }
}
