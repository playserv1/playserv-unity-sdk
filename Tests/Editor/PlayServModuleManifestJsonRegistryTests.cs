using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServModuleManifestJsonRegistryTests
    {
        private const string TestRootAssetPath = "Assets/PlayServTestArtifacts";
        private const string TestDescriptorAssetPath =
            TestRootAssetPath + "/ExternalModule/module.playserv.json";

        [SetUp]
        public void SetUp()
        {
            DeleteTestAssets();
            PlayServModuleManifestJsonRegistry.Reload();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTestAssets();
            PlayServModuleManifestJsonRegistry.Reload();
        }

        [Test]
        public void Reload_ReadsEveryPackageDescriptorWithoutErrors()
        {
            PlayServModuleManifestJsonRegistry.Reload();

            var errors = PlayServModuleManifestJsonRegistry.Diagnostics
                .Where(diagnostic => diagnostic.IsError)
                .Select(diagnostic => diagnostic.Message)
                .ToArray();

            Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors));
            Assert.That(PlayServModuleManifestJsonRegistry.DescriptorAssetPaths, Is.Not.Empty);
            Assert.That(
                PlayServModuleManifest.RuntimeModules.Count,
                Is.EqualTo(PlayServModuleManifestJsonRegistry.DescriptorAssetPaths.Count));

            foreach (var module in PlayServModuleManifest.RuntimeModules)
            {
                Assert.That(module.Id, Is.Not.Empty);
                Assert.That(module.DisableDefine, Does.StartWith("PLAYSERV_"));
                Assert.That(module.DescriptorAssetPath, Does.EndWith("module.playserv.json"));
            }
        }

        [Test]
        public void Reload_DiscoversAndNormalizesExternalDescriptor()
        {
            var absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                TestDescriptorAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            File.WriteAllText(
                absolutePath,
                "{\n" +
                "  \"id\": \"  test-external  \",\n" +
                "  \"schemaVersion\": 1,\n" +
                "  \"order\": 999,\n" +
                "  \"label\": \"  External Test  \",\n" +
                "  \"minSdkVersion\": \" 0.3.0 \",\n" +
                "  \"supportedPlatforms\": [\" Editor \", \" iOS \"],\n" +
                "  \"conflictsWith\": [\" google-sign-in \"],\n" +
                "  \"capabilities\": [\" identity.external \"],\n" +
                "  \"requiresPackages\": [\" com.unity.nuget.newtonsoft-json \"],\n" +
                "  \"disableDefine\": \"  PLAYSERV_MODULE_DISABLED_TEST_EXTERNAL  \",\n" +
                "  \"defaultEnabled\": true,\n" +
                "  \"visibleInSettings\": true,\n" +
                "  \"visibleInExport\": true,\n" +
                "  \"assetPaths\": [\" /Runtime\\\\Feature/ \"],\n" +
                "  \"dependencyIds\": [\" client-execution \"],\n" +
                "  \"hiddenDependencyModuleIds\": [\" rpc-core \"],\n" +
                "  \"profiles\": [\" full-sdk \"],\n" +
                "  \"rootAssemblyReference\": \" Test.External \"\n" +
                "}\n");
            AssetDatabase.ImportAsset(TestDescriptorAssetPath, ImportAssetOptions.ForceSynchronousImport);

            PlayServModuleManifestJsonRegistry.Reload();

            Assert.That(PlayServModuleManifest.TryGet("test-external", out var module), Is.True);
            Assert.That(module.Label, Is.EqualTo("External Test"));
            Assert.That(module.DisableDefine, Is.EqualTo("PLAYSERV_MODULE_DISABLED_TEST_EXTERNAL"));
            Assert.That(module.AssetPaths, Is.EqualTo(new[] { "Runtime/Feature" }));
            Assert.That(module.DependencyIds, Is.EqualTo(new[] { "client-execution" }));
            Assert.That(module.HiddenDependencyModuleIds, Is.EqualTo(new[] { "rpc-core" }));
            Assert.That(module.ProfileIds, Is.EqualTo(new[] { "full-sdk" }));
            Assert.That(module.RootAssemblyReference, Is.EqualTo("Test.External"));
            Assert.That(module.DescriptorAssetPath, Is.EqualTo(TestDescriptorAssetPath));
            Assert.That(module.SchemaVersion, Is.EqualTo(1));
            Assert.That(module.MinSdkVersion, Is.EqualTo("0.3.0"));
            Assert.That(module.SupportedPlatforms, Is.EqualTo(new[] { "Editor", "iOS" }));
            Assert.That(module.ConflictsWith, Is.EqualTo(new[] { "google-sign-in" }));
            Assert.That(module.Capabilities, Is.EqualTo(new[] { "identity.external" }));
            Assert.That(module.RequiresPackages, Is.EqualTo(new[] { "com.unity.nuget.newtonsoft-json" }));
        }

        [Test]
        public void Reload_OrdersDependenciesBeforeDependentsRegardlessOfUiOrder()
        {
            WriteDescriptor(
                "TopologicalDependency",
                "test-topological-dependency",
                900,
                Array.Empty<string>());
            WriteDescriptor(
                "TopologicalDependent",
                "test-topological-dependent",
                1,
                new[] { "test-topological-dependency" });

            PlayServModuleManifestJsonRegistry.Reload();

            var ids = PlayServModuleManifest.RuntimeModules.Select(module => module.Id).ToArray();
            Assert.That(
                Array.IndexOf(ids, "test-topological-dependency"),
                Is.LessThan(Array.IndexOf(ids, "test-topological-dependent")));
        }

        [Test]
        public void Reload_ReportsCyclicDependencyPath()
        {
            WriteDescriptor(
                "CycleA",
                "test-cycle-a",
                900,
                new[] { "test-cycle-b" });
            WriteDescriptor(
                "CycleB",
                "test-cycle-b",
                901,
                new[] { "test-cycle-a" });

            PlayServModuleManifestJsonRegistry.Reload();

            var cycleDiagnostic = PlayServModuleManifestJsonRegistry.Diagnostics
                .FirstOrDefault(diagnostic =>
                    diagnostic.IsError &&
                    diagnostic.Message.Contains("Cyclic module dependency detected"));

            Assert.That(cycleDiagnostic, Is.Not.Null);
            Assert.That(cycleDiagnostic.Message, Does.Contain("test-cycle-a"));
            Assert.That(cycleDiagnostic.Message, Does.Contain("test-cycle-b"));
        }

        [Test]
        public void Reload_RejectsModuleThatRequiresNewerSdk()
        {
            WriteDescriptor(
                "FutureSdk",
                "test-future-sdk",
                900,
                Array.Empty<string>(),
                minSdkVersion: "999.0.0");

            PlayServModuleManifestJsonRegistry.Reload();

            Assert.That(PlayServModuleManifest.TryGet("test-future-sdk", out _), Is.False);
            Assert.That(
                PlayServModuleManifestJsonRegistry.Diagnostics.Any(diagnostic =>
                    diagnostic.IsError &&
                    diagnostic.Message.Contains("requires PlayServ SDK 999.0.0")),
                Is.True);
        }

        [Test]
        public void Availability_RequiresDeclaredUpmPackages()
        {
            WriteDescriptor(
                "MissingPackage",
                "test-missing-package",
                900,
                Array.Empty<string>(),
                requiresPackages: new[] { "com.playserv.missing-package" });

            PlayServModuleManifestJsonRegistry.Reload();

            Assert.That(
                PlayServEditorModuleAvailability.IsRuntimeModuleAvailable("test-missing-package"),
                Is.False);
        }

        [Test]
        public void NormalizeDependencies_DisablesLaterConflictingModule()
        {
            WriteDescriptor(
                "ConflictFirst",
                "test-conflict-first",
                900,
                Array.Empty<string>(),
                conflictsWith: new[] { "test-conflict-second" });
            WriteDescriptor(
                "ConflictSecond",
                "test-conflict-second",
                901,
                Array.Empty<string>());
            PlayServModuleManifestJsonRegistry.Reload();

            var state = new PlayServRuntimeModuleState();
            foreach (var module in PlayServModuleManifest.RuntimeModules)
                state.SetEnabled(module.Id, false);
            state.SetEnabled("test-conflict-first", true);
            state.SetEnabled("test-conflict-second", true);

            PlayServRuntimeModuleDefines.NormalizeDependencies(state);

            Assert.That(state.IsEnabled("test-conflict-first"), Is.True);
            Assert.That(state.IsEnabled("test-conflict-second"), Is.False);
        }

        private static void WriteDescriptor(
            string directoryName,
            string moduleId,
            int order,
            string[] dependencyIds,
            string minSdkVersion = "0.3.0",
            string[] conflictsWith = null,
            string[] requiresPackages = null)
        {
            var descriptorAssetPath =
                $"{TestRootAssetPath}/{directoryName}/module.playserv.json";
            var absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                descriptorAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            var dependencies = string.Join(
                ", ",
                dependencyIds.Select(dependencyId => $"\"{dependencyId}\""));
            var conflicts = string.Join(
                ", ",
                (conflictsWith ?? Array.Empty<string>())
                .Select(conflictId => $"\"{conflictId}\""));
            var packages = string.Join(
                ", ",
                (requiresPackages ?? Array.Empty<string>())
                .Select(packageId => $"\"{packageId}\""));
            File.WriteAllText(
                absolutePath,
                "{\n" +
                "  \"schemaVersion\": 1,\n" +
                $"  \"id\": \"{moduleId}\",\n" +
                $"  \"order\": {order},\n" +
                $"  \"label\": \"{moduleId}\",\n" +
                "  \"description\": \"Test module\",\n" +
                $"  \"minSdkVersion\": \"{minSdkVersion}\",\n" +
                "  \"supportedPlatforms\": [\"Editor\"],\n" +
                $"  \"conflictsWith\": [{conflicts}],\n" +
                "  \"capabilities\": [],\n" +
                $"  \"requiresPackages\": [{packages}],\n" +
                $"  \"disableDefine\": \"PLAYSERV_MODULE_DISABLED_{moduleId.ToUpperInvariant().Replace('-', '_')}\",\n" +
                "  \"defaultEnabled\": false,\n" +
                "  \"isServerModule\": false,\n" +
                "  \"visibleInSettings\": true,\n" +
                "  \"visibleInExport\": true,\n" +
                "  \"assetPaths\": [],\n" +
                $"  \"dependencyIds\": [{dependencies}],\n" +
                "  \"hiddenDependencyAssetPaths\": [],\n" +
                "  \"hiddenDependencyModuleIds\": [],\n" +
                "  \"profiles\": [],\n" +
                "  \"rootAssemblyReference\": \"\"\n" +
                "}\n");
            AssetDatabase.ImportAsset(descriptorAssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteTestAssets()
        {
            if (AssetDatabase.IsValidFolder(TestRootAssetPath))
                AssetDatabase.DeleteAsset(TestRootAssetPath);
        }
    }
}
