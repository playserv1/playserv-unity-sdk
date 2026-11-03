using NUnit.Framework;
using Playserv.Editor;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServConfigEndpointOverrideTests
    {
        [Test]
        public void ProductionDefaultsUseServingRuntime()
        {
            Assert.That(PlayServPackageDefaultsProvider.LoadSettingsOrDefault("Prod").BackendServerAddress,
                Is.EqualTo("wss://platform.playserv.com/ws"));
        }

        [TestCase("wss://proxy.playserv.io/ws", "wss://platform.playserv.com/ws")]
        [TestCase("wss://custom.example.test/ws", "wss://custom.example.test/ws")]
        public void LegacyRuntimeDefaultsMigrateWithoutChangingCustomEndpoints(string oldAddress, string expected)
        {
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"backendServerAddress\":\"" + oldAddress + "\",\"deployApiServerAddress\":\"https://retired.test\",\"schemaApiServerAddress\":\"https://retired.test\"}", config);
                Assert.That(config.ToSettings().BackendServerAddress, Is.EqualTo(expected));
                Assert.That(config.ToSettings().DeployApiServerAddress, Is.Empty);
                Assert.That(config.ToSettings().SchemaApiServerAddress, Is.Empty);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void V1EndpointFieldsAreNotSerialized()
        {
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            var defaults = ScriptableObject.CreateInstance<PlayServPackageDefaults>();
            try
            {
                foreach (var asset in new Object[] { config, defaults })
                {
                    var serialized = new SerializedObject(asset);
                    Assert.That(serialized.FindProperty("deployApiServerAddress"), Is.Null);
                    Assert.That(serialized.FindProperty("schemaApiServerAddress"), Is.Null);
                }
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(defaults); }
        }

        [TestCase("Dev", "https://dashboard.dev.playserv.com")]
        [TestCase("Prod", "https://dashboard.playserv.com")]
        public void DefaultDashboardUsesCurrentPlatformOrigin(string environment, string expected)
        {
            Assert.That(PlayServPackageDefaultsProvider.LoadSettingsOrDefault(environment).DashboardAddress.TrimEnd('/'), Is.EqualTo(expected));
        }

        private static string ActiveEnvironmentPrefKey =>
            PlayServEnvironmentClientTokens.ActiveEnvironmentPreference;

        [Test]
        public void ResolveEditorSettings_PreservesConfigEndpointOverrides()
        {
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            try
            {
                var configured = config.ToSettings();
                configured.BackendServerAddress = "wss://custom.example.test/ws";
                configured.DeployApiServerAddress = "https://deploy.example.test";
                configured.SchemaApiServerAddress = "https://schema.example.test";
                configured.DashboardAddress = "https://dashboard.example.test";
                config.ApplySettings(configured);

                var resolved = PlayServSettingsResolver.ResolveEditorSettings(config);

                Assert.That(
                    resolved.BackendServerAddress,
                    Is.EqualTo(configured.BackendServerAddress));
                Assert.That(
                    resolved.DeployApiServerAddress,
                    Is.Empty);
                Assert.That(
                    resolved.SchemaApiServerAddress,
                    Is.Empty);
                Assert.That(
                    resolved.DashboardAddress,
                    Is.EqualTo(configured.DashboardAddress));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ApplyActiveEnvironmentDefaults_UpdatesConfigExplicitly()
        {
            var hadOriginal = EditorPrefs.HasKey(ActiveEnvironmentPrefKey);
            var original = EditorPrefs.GetString(ActiveEnvironmentPrefKey);
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            try
            {
                EditorPrefs.SetString(
                    ActiveEnvironmentPrefKey,
                    PlayServPackageDefaultsProvider.ProductionEnvironmentName);
                var expected = PlayServPackageDefaultsProvider.LoadSettingsOrDefault(
                    PlayServPackageDefaultsProvider.ProductionEnvironmentName);

                Assert.That(
                    PlayServPackageEnvironmentProvider
                        .TryApplyActiveEnvironmentDefaults(config),
                    Is.True);
                Assert.That(
                    config.BackendServerAddress,
                    Is.EqualTo(expected.BackendServerAddress));
                Assert.That(
                    config.DeployApiServerAddress,
                    Is.EqualTo(expected.DeployApiServerAddress));
                Assert.That(
                    config.SchemaApiServerAddress,
                    Is.EqualTo(expected.SchemaApiServerAddress));
                Assert.That(
                    config.DashboardAddress,
                    Is.EqualTo(expected.DashboardAddress));
            }
            finally
            {
                if (hadOriginal)
                    EditorPrefs.SetString(ActiveEnvironmentPrefKey, original);
                else
                    EditorPrefs.DeleteKey(ActiveEnvironmentPrefKey);

                Object.DestroyImmediate(config);
            }
        }
    }
}
