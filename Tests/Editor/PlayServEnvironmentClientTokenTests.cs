using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Text;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Wrapper;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServEnvironmentClientTokenTests
    {
        private string _folder;
        private PlayServConfig _config;
        private string _activeEnvironment;
        private bool _hadEnvironment;
        private string _processEnvironment;
        private string _processToken;

        [SetUp]
        public void SetUp()
        {
            _processEnvironment = Environment.GetEnvironmentVariable("PLAYSERV_ENVIRONMENT");
            _processToken = Environment.GetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN");
            Environment.SetEnvironmentVariable("PLAYSERV_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN", null);
            _hadEnvironment = EditorPrefs.HasKey(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference);
            _activeEnvironment = EditorPrefs.GetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference);
            _folder = "Assets/PlayServTokenTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", _folder.Substring("Assets/".Length));
            _config = ScriptableObject.CreateInstance<PlayServConfig>();
            AssetDatabase.CreateFolder(_folder, "Resources");
            AssetDatabase.CreateAsset(_config, _folder + "/Resources/PlayServConfig.asset");
        }

        [TearDown]
        public void TearDown()
        {
            FinishBuild();
            foreach (var environment in new[] { "Dev", "Prod" })
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.PreferenceKey(_config, environment));
            EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(_config));
            AssetDatabase.DeleteAsset(_folder);
            if (_hadEnvironment)
                EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, _activeEnvironment);
            else
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference);
            Environment.SetEnvironmentVariable("PLAYSERV_ENVIRONMENT", _processEnvironment);
            Environment.SetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN", _processToken);
        }

        [Test]
        public void SwitchingToUnconfiguredEnvironment_DoesNotReusePreviousToken()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_environment_dev_fixture");
            SwitchEnvironment("Prod");

            Assert.That(_config.ClientToken, Is.Null.Or.Empty);
        }

        [Test]
        public void SwitchingBack_RestoresIndependentToken()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_environment_dev_fixture");
            SwitchEnvironment("Prod");
            _config.SetClientToken("pk_environment_prod_fixture");
            SwitchEnvironment("Dev");

            Assert.That(_config.ToSettings().ClientToken, Is.EqualTo("pk_environment_dev_fixture"));
        }

        [Test]
        public void SettingsApplication_DoesNotSerializeLocalToken()
        {
            SwitchEnvironment("Dev");
            var settings = _config.ToSettings();
            settings.ClientToken = "pk_local_settings_fixture";
            settings.BackendServerAddress = "wss://custom.example.test/ws";
            _config.ApplySettings(settings);
            AssetDatabase.SaveAssetIfDirty(_config);

            Assert.That(_config.ClientToken, Is.EqualTo(settings.ClientToken));
            Assert.That(PlayServSettingsResolver.ResolveEditorSettings(_config).ClientToken, Is.EqualTo(settings.ClientToken));
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            StringAssert.DoesNotContain(settings.ClientToken, System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(_config)));
            Assert.That(PlayServSettingsResolver.ResolveEditorSettings(_config).BackendServerAddress,
                Is.EqualTo(settings.BackendServerAddress));
        }

        [Test]
        public void ExplicitClear_SurvivesSwitchingAndAssetReload()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_old_fixture");
            _config.SetClientToken(string.Empty);
            SwitchEnvironment("Prod");
            _config.SetClientToken("pk_prod_fixture");
            SwitchEnvironment("Dev");
            var path = AssetDatabase.GetAssetPath(_config);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            _config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(path);
            Assert.That(PlayServSettingsResolver.ResolveEditorSettings(_config).ClientToken, Is.Empty);
        }

        [Test]
        public void LegacyToken_MigratesOnlyOnceToSelectedEnvironment()
        {
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, "Prod");
            var serialized = new SerializedObject(_config);
            serialized.FindProperty("clientToken").stringValue = "pk_legacy_fixture";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_config.ClientToken, Is.EqualTo("pk_legacy_fixture"));
            SwitchEnvironment("Dev");
            Assert.That(_config.ClientToken, Is.Empty);
            SwitchEnvironment("Prod");
            Assert.That(_config.ClientToken, Is.EqualTo("pk_legacy_fixture"));
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
        }

        [Test]
        public void Migration_DoesNotOverwriteExistingLocalValue()
        {
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, "Dev");
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.PreferenceKey(_config, "Dev"), "pk_saved_fixture");
            var serialized = new SerializedObject(_config);
            serialized.FindProperty("clientToken").stringValue = "pk_legacy_fixture";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_config.ClientToken, Is.EqualTo("pk_saved_fixture"));
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
        }

        [Test]
        public void SwitchingEnvironment_MigratesUnreadLegacyConfigsUnderPreviousEnvironment()
        {
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, "Dev");
            var other = ScriptableObject.CreateInstance<PlayServConfig>();
            AssetDatabase.CreateAsset(other, _folder + "/Unread.asset");
            try
            {
                var serialized = new SerializedObject(other);
                serialized.FindProperty("clientToken").stringValue = "pk_unread_dev_fixture";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                SwitchEnvironment("Prod");
                Assert.That(other.ClientToken, Is.Empty, "Unread Dev credentials must not migrate to Prod.");
                SwitchEnvironment("Dev");
                Assert.That(other.ClientToken, Is.EqualTo("pk_unread_dev_fixture"));
            }
            finally
            {
                foreach (var environment in new[] { "Dev", "Prod" })
                    EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.PreferenceKey(other, environment));
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(other));
            }
        }

        [Test]
        public void UnavailableLegacyEnvironment_UsesSelectableDevForMigration()
        {
            var key = PlayServEnvironmentClientTokens.LegacyEnvironmentPreference;
            var hadValue = EditorPrefs.HasKey(key);
            var original = EditorPrefs.GetString(key);
            try
            {
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference);
                EditorPrefs.SetString(key, "does-not-exist-fixture");
                var serialized = new SerializedObject(_config);
                serialized.FindProperty("clientToken").stringValue = "pk_legacy_fixture";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(PlayServEnvironmentClientTokens.ActiveEnvironment, Is.EqualTo("Dev"));
                Assert.That(_config.ClientToken, Is.EqualTo("pk_legacy_fixture"));
            }
            finally
            {
                if (hadValue) EditorPrefs.SetString(key, original);
                else EditorPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void BuildPreparation_DoesNotRequireUnusedBlankConfig()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_runtime_fixture");
            var other = ScriptableObject.CreateInstance<PlayServConfig>();
            AssetDatabase.CreateAsset(other, _folder + "/Unused.asset");
            try { Assert.DoesNotThrow(PrepareBuild); }
            finally
            {
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.PreferenceKey(other, "Dev"));
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(other));
            }
        }

        [Test]
        public void Store_IsolatesProjectConfigAndNormalizedEnvironment()
        {
            var key = PlayServEnvironmentClientTokens.PreferenceKey("project-a", "config-a", "Dev");
            Assert.That(PlayServEnvironmentClientTokens.PreferenceKey("project-a", "config-a", "development"), Is.EqualTo(key));
            Assert.That(PlayServEnvironmentClientTokens.PreferenceKey("project-b", "config-a", "Dev"), Is.Not.EqualTo(key));
            Assert.That(PlayServEnvironmentClientTokens.PreferenceKey("project-a", "config-b", "Dev"), Is.Not.EqualTo(key));
            Assert.That(PlayServEnvironmentClientTokens.PreferenceKey("project-a", "config-a", "Prod"), Is.Not.EqualTo(key));
            var other = ScriptableObject.CreateInstance<PlayServConfig>();
            AssetDatabase.CreateAsset(other, _folder + "/Other.asset");
            try
            {
                SwitchEnvironment("Dev");
                _config.SetClientToken("pk_first_fixture");
                Assert.That(other.ClientToken, Is.Empty);
                other.SetClientToken("pk_second_fixture");
                Assert.That(_config.ClientToken, Is.EqualTo("pk_first_fixture"));
            }
            finally
            {
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.PreferenceKey(other, "Dev"));
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(other));
            }
        }

        [Test]
        public void ProcessOverride_IsReadOnlyAndDoesNotPersist()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_local_fixture");
            Environment.SetEnvironmentVariable("PLAYSERV_ENVIRONMENT", "Prod");
            Environment.SetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN", "pk_ci_fixture");
            Assert.That(_config.ToSettings().ClientToken, Is.EqualTo("pk_ci_fixture"));
            _config.ApplySettings(_config.ToSettings());
            Assert.That(PlayServEnvironmentClientTokens.LocalEnvironment, Is.EqualTo("Dev"));
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            Environment.SetEnvironmentVariable("PLAYSERV_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN", null);
            Assert.That(_config.ClientToken, Is.EqualTo("pk_local_fixture"));
            SwitchEnvironment("Prod");
            Assert.That(_config.ClientToken, Is.Empty);
        }

        [TestCase("Prod", null)]
        [TestCase(null, "pk_ci_fixture")]
        [TestCase("unknown-environment", "pk_ci_fixture")]
        [TestCase("Prod", "sk_not_allowed_fixture")]
        public void InvalidProcessOverride_FailsWithoutCredentialInMessage(string environment, string token)
        {
            Environment.SetEnvironmentVariable("PLAYSERV_ENVIRONMENT", environment);
            Environment.SetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN", token);
            var error = Assert.Throws<InvalidOperationException>(() => _config.ToSettings());
            if (token != null)
                StringAssert.DoesNotContain(token, error.Message);
        }

        [Test]
        public void TransientConfig_RemainsSerializedAndIndependent()
        {
            var transient = ScriptableObject.CreateInstance<PlayServConfig>();
            try
            {
                transient.SetClientToken("pk_transient_fixture");
                Assert.That(transient.ToSettings().ClientToken, Is.EqualTo("pk_transient_fixture"));
                Assert.That(new SerializedObject(transient).FindProperty("clientToken").stringValue,
                    Is.EqualTo("pk_transient_fixture"));
            }
            finally { UnityEngine.Object.DestroyImmediate(transient); }
        }

        [Test]
        public void BuildPreparation_BakesOnlyActiveTokenAndRestoresAsset()
        {
            SwitchEnvironment("Prod");
            _config.SetClientToken("pk_inactive_build_fixture");
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_active_build_fixture");
            PrepareBuild();
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue,
                Is.EqualTo("pk_active_build_fixture"));
            StringAssert.DoesNotContain("pk_inactive_build_fixture",
                System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(_config)));
            FinishBuild();
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            Assert.That(_config.ClientToken, Is.EqualTo("pk_active_build_fixture"));
        }

        [TestCase("")]
        [TestCase("sk_private_fixture")]
        [TestCase("a.b.c")]
        public void BuildPreparation_RejectsMissingOrNonPublicToken(string token)
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken(token);
            var error = Assert.Throws<BuildFailedException>(PrepareBuild);
            if (token.Length > 0)
                StringAssert.DoesNotContain(token, error.Message);
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            Assert.That(System.IO.File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.False);
        }

        [Test]
        public void InterruptedBuild_RecoversFromJournalWithoutOverwritingOtherSettings()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_build_fixture");
            PrepareBuild();
            var serialized = new SerializedObject(_config);
            serialized.FindProperty("gameVersion").stringValue = "9.8.7";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(_config);
            Assert.That(System.IO.File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.True);
            // No successful post-build callback: recover solely from the persistent journal.
            PlayServClientTokenBuildProcessor.Restore();
            Assert.That(_config.GameVersion, Is.EqualTo("9.8.7"));
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            Assert.That(System.IO.File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.False);
            PlayServClientTokenBuildProcessor.Restore();
        }

        [Test]
        public void BuildRecovery_PreservesConflictingTokenEdit()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_build_fixture");
            PrepareBuild();
            var serialized = new SerializedObject(_config);
            serialized.FindProperty("clientToken").stringValue = "pk_changed_by_another_tool_fixture";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            LogAssert.Expect(LogType.Warning,
                "[PlayServ] Client Token changed after build preparation; preserving the newer edit. Review the config asset before committing it.");
            FinishBuild();
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue,
                Is.EqualTo("pk_changed_by_another_tool_fixture"));
            var saved = File.ReadAllText(AssetDatabase.GetAssetPath(_config));
            StringAssert.Contains("pk_changed_by_another_tool_fixture", saved);
            StringAssert.DoesNotContain("pk_build_fixture", saved);
        }

        [Test]
        public void BuildRecovery_SavesOriginalValueAlreadyRestoredOnlyInMemory()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_durable_recovery_fixture");
            PrepareBuild();
            var path = AssetDatabase.GetAssetPath(_config);
            var serialized = new SerializedObject(_config);
            serialized.FindProperty("clientToken").stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            StringAssert.Contains("pk_durable_recovery_fixture", File.ReadAllText(path));
            PlayServClientTokenBuildProcessor.Restore();
            StringAssert.DoesNotContain("pk_durable_recovery_fixture", File.ReadAllText(path));
            Assert.That(File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.False);
        }

        [Test]
        public void MissingPostBuildCallback_IdleEditorRestoresFromJournal()
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_idle_recovery_fixture");
            PrepareBuild();
            PlayServClientTokenBuildProcessor.RecoverWhenIdle(false, false, false);
            Assert.That(File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.False);
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            StringAssert.DoesNotContain("pk_idle_recovery_fixture", File.ReadAllText(AssetDatabase.GetAssetPath(_config)));
        }

        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void Recovery_WaitsForBuildCompilationAndImportBeforeRestoring(
            bool isBuildingPlayer, bool isCompiling, bool isUpdating)
        {
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_busy_recovery_fixture");
            PrepareBuild();
            var path = AssetDatabase.GetAssetPath(_config);
            var journal = File.ReadAllText(PlayServClientTokenBuildProcessor.JournalPath);
            var asset = File.ReadAllText(path);

            PlayServClientTokenBuildProcessor.RecoverWhenIdle(isBuildingPlayer, isCompiling, isUpdating);

            Assert.That(File.ReadAllText(PlayServClientTokenBuildProcessor.JournalPath), Is.EqualTo(journal));
            Assert.That(File.ReadAllText(path), Is.EqualTo(asset));
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue,
                Is.EqualTo("pk_busy_recovery_fixture"));

            PlayServClientTokenBuildProcessor.RecoverWhenIdle(false, false, false);

            Assert.That(File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.False);
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            StringAssert.DoesNotContain("pk_busy_recovery_fixture", File.ReadAllText(path));
            PlayServClientTokenBuildProcessor.RecoverWhenIdle(false, false, false);
        }

        [Test]
        public void RecoveryCallback_IsRegisteredForEditorUpdatesAndShutdown()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(PlayServClientTokenBuildProcessor).TypeHandle);
            var callback = typeof(PlayServClientTokenBuildProcessor).GetMethod("RecoverWhenIdle",
                BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            Assert.That(callback, Is.Not.Null);
            Assert.That(EditorApplication.update.GetInvocationList().Count(item => item.Method == callback), Is.EqualTo(1));
            var quitting = typeof(EditorApplication).GetField("m_QuittingEvent", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(quitting, Is.Not.Null, "Unity Editor quitting event must be inspectable.");
            var trackedEvent = quitting.GetValue(null);
            Assert.That(GetTrackedEventHandlers(trackedEvent).Count(item => item.Method == callback), Is.EqualTo(1));
        }

        private static IEnumerable<Delegate> GetTrackedEventHandlers(object value)
        {
            if (value is Delegate handler)
                return handler.GetInvocationList();
            if (value is System.Collections.IEnumerable handlers && !(value is string))
                return handlers.Cast<object>().SelectMany(GetTrackedEventHandlers);
            if (value == null || !value.GetType().FullName.StartsWith("UnityEditor.EventWithPerformanceTracker", StringComparison.Ordinal))
                return Enumerable.Empty<Delegate>();
            // Unity 2021 stores the delegate directly; Unity 6 wraps it with a profiler handle.
            return value.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(field => GetTrackedEventHandlers(field.GetValue(value)));
        }

        [Test]
        public void CustomProvider_KeepsSerializedConfigBehaviorAndBuildOwnership()
        {
            var provider = new CustomSettingsProvider();
            PlayServClientProjectSettingsRegistry.Register(provider);
            try
            {
                _config.SetClientToken("pk_custom_config_fixture");
                Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue,
                    Is.EqualTo("pk_custom_config_fixture"));
                Assert.That(PlayServSettingsResolver.ResolveEditorSettings(_config).ClientToken,
                    Is.EqualTo("pk_custom_provider_fixture"));
                PrepareBuild();
                Assert.That(System.IO.File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.False);
            }
            finally { PlayServClientProjectSettingsRegistry.Clear(provider); }
        }

        private sealed class CustomSettingsProvider : IPlayServClientProjectSettingsProvider
        {
            public bool TryResolveSettings(PlayServConfig config, out PlayServSettings settings)
            {
                settings = config.ToSettings();
                settings.ClientToken = "pk_custom_provider_fixture";
                return true;
            }

            public bool TryDrawProjectConfigUi(out bool changed) { changed = false; return false; }
        }

        [Test]
        [Category("BuildSmoke")]
        [Timeout(600000)]
        public void PlayerBuild_ContainsActiveTokenOnlyAndRestoresSourceConfig()
        {
            if (Environment.GetEnvironmentVariable("PLAYSERV_RUN_TOKEN_BUILD_TESTS") != "1")
                Assert.Ignore("Set PLAYSERV_RUN_TOKEN_BUILD_TESTS=1 to build the environment-token smoke player.");
            SwitchEnvironment("Prod");
            _config.SetClientToken("pk_not_in_player_build_fixture");
            SwitchEnvironment("Dev");
            _config.SetClientToken("pk_in_player_build_fixture");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var output = Path.GetFullPath("Temp/PlayServTokenSmoke/Player.app");
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var scenePath = _folder + "/Smoke.unity";
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    target = BuildTarget.StandaloneOSX,
                    locationPathName = output,
                    options = BuildOptions.Development
                });
                Assert.That(report.summary.result, Is.EqualTo(BuildResult.Succeeded));
                var assets = Directory.GetFiles(output, "*.assets", SearchOption.AllDirectories);
                var containsActive = false;
                foreach (var asset in assets)
                {
                    var content = Encoding.UTF8.GetString(File.ReadAllBytes(asset));
                    containsActive |= content.Contains("pk_in_player_build_fixture");
                    StringAssert.DoesNotContain("pk_not_in_player_build_fixture", content);
                }
                Assert.That(containsActive, Is.True, "The player must contain the selected runtime credential.");
                Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
                Assert.That(File.Exists(PlayServClientTokenBuildProcessor.JournalPath), Is.False);
            }
            finally
            {
                PlayServClientTokenBuildProcessor.Restore();
                if (setup.Any(item => item.isLoaded && item.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static void PrepareBuild()
        {
            foreach (var callback in TypeCache.GetTypesDerivedFrom<IPreprocessBuildWithReport>()
                         .Where(type => type.Namespace == "Playserv.Editor" && !type.IsAbstract)
                         .Select(type => (IPreprocessBuildWithReport)Activator.CreateInstance(type, true))
                         .OrderBy(callback => callback.callbackOrder))
                callback.OnPreprocessBuild(null);
        }

        private static void FinishBuild()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<IPostprocessBuildWithReport>()
                         .Where(type => type.Namespace == "Playserv.Editor" && !type.IsAbstract))
                ((IPostprocessBuildWithReport)Activator.CreateInstance(type, true)).OnPostprocessBuild(null);
        }

        private void SwitchEnvironment(string environment)
        {
            typeof(PlayServPackageEnvironmentProvider)
                .GetMethod("TryApplyEnvironmentDefaults", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { _config, environment });
        }
    }
}
