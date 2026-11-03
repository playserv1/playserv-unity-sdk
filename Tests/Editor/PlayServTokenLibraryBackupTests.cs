using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServTokenLibraryBackupTests
    {
        private string _folder;
        private PlayServConfig _config;
        private PlayServConfig _other;
        private string _environment;
        private bool _hadEnvironment;
        private readonly Dictionary<string, string> _variables = new Dictionary<string, string>();

        [SetUp]
        public void SetUp()
        {
            foreach (var name in new[] { "PLAYSERV_ENVIRONMENT", "PLAYSERV_CLIENT_TOKEN", "PLAYSERV_API_KEY" })
            {
                _variables[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
            _hadEnvironment = EditorPrefs.HasKey(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference);
            _environment = EditorPrefs.GetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference);
            Select("Dev");
            _folder = "Assets/TokenLibraryFixture" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_folder));
            _config = CreateConfig("First");
            _other = CreateConfig("Second");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var config in new[] { _config, _other })
            {
                foreach (var environment in new[] { "Dev", "Prod" })
                foreach (var key in new[] { ClientKey(config, environment), ServerKey(config, environment) })
                {
                    EditorPrefs.DeleteKey(key);
                    File.Delete(BackupPath(key));
                }
                EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(config));
            }
            AssetDatabase.DeleteAsset(_folder);
            if (_hadEnvironment) EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, _environment);
            else EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference);
            foreach (var pair in _variables) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        [Test]
        public void ClientEdits_WriteLibraryImmediatelyAndRestoreAfterPreferencesAreLost()
        {
            _config.SetClientToken("pk_first_fixture");
            _config.SetClientToken("pk_updated_fixture");
            AssertBackup(ClientKey(_config, "Dev"), "pk_updated_fixture");
            EditorPrefs.DeleteKey(ClientKey(_config, "Dev"));
            EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(_config));
            var path = AssetDatabase.GetAssetPath(_config);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            _config = AssetDatabase.LoadAssetAtPath<PlayServConfig>(path);

            Assert.That(_config.ClientToken, Is.EqualTo("pk_updated_fixture"));
            Assert.That(EditorPrefs.GetString(ClientKey(_config, "Dev")), Is.EqualTo("pk_updated_fixture"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("pk_updated_fixture"));
        }

        [Test]
        public void ServerEdits_WriteLibraryImmediatelyAndRestoreAfterPreferencesAreLost()
        {
            PlayServDeploymentSettings.SetLocal(_config, "Dev", "sk_first_fixture");
            PlayServDeploymentSettings.SetLocal(_config, "Dev", "sk_updated_fixture");
            AssertBackup(ServerKey(_config, "Dev"), "sk_updated_fixture");
            EditorPrefs.DeleteKey(ServerKey(_config, "Dev"));

            Assert.That(PlayServDeploymentSettings.GetLocal(_config, "Dev"), Is.EqualTo("sk_updated_fixture"));
            Assert.That(EditorPrefs.GetString(ServerKey(_config, "Dev")), Is.EqualTo("sk_updated_fixture"));
            Assert.That(JsonUtility.ToJson(_config.ToSettings()), Does.Not.Contain("sk_updated_fixture"));
        }

        [Test]
        public void ClearingTokens_PersistsEmptyBackupsAndCannotRestoreTheOldValues()
        {
            _config.SetClientToken("pk_old_fixture");
            PlayServDeploymentSettings.SetLocal(_config, "Dev", "sk_old_fixture");
            _config.SetClientToken("");
            PlayServDeploymentSettings.SetLocal(_config, "Dev", "");
            AssertBackup(ClientKey(_config, "Dev"), "");
            AssertBackup(ServerKey(_config, "Dev"), "");
            EditorPrefs.DeleteKey(ClientKey(_config, "Dev"));
            EditorPrefs.DeleteKey(ServerKey(_config, "Dev"));
            EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(_config));

            Assert.That(_config.ClientToken, Is.Empty);
            Assert.That(PlayServDeploymentSettings.GetLocal(_config, "Dev"), Is.Empty);
        }

        [Test]
        public void Recovery_IsolatedByConfigAndEnvironment()
        {
            foreach (var config in new[] { _config, _other })
            foreach (var environment in new[] { "Dev", "Prod" })
            {
                Select(environment);
                var suffix = config.name + environment;
                config.SetClientToken("pk_" + suffix);
                PlayServDeploymentSettings.SetLocal(config, environment, "sk_" + suffix);
                EditorPrefs.DeleteKey(ClientKey(config, environment));
                EditorPrefs.DeleteKey(ServerKey(config, environment));
            }
            foreach (var config in new[] { _config, _other })
            foreach (var environment in new[] { "Prod", "Dev" })
            {
                Select(environment);
                var suffix = config.name + environment;
                Assert.That(config.ClientToken, Is.EqualTo("pk_" + suffix));
                Assert.That(PlayServDeploymentSettings.GetLocal(config, environment), Is.EqualTo("sk_" + suffix));
            }
        }

        [Test]
        public void ExistingPreferences_AreBackedUpOnFirstReadWithoutReplacingTheirValues()
        {
            EditorPrefs.SetString(ClientKey(_config, "Dev"), "pk_existing_fixture");
            EditorPrefs.SetString(ServerKey(_config, "Dev"), "sk_existing_fixture");
            Assert.That(_config.ClientToken, Is.EqualTo("pk_existing_fixture"));
            Assert.That(PlayServDeploymentSettings.GetLocal(_config, "Dev"), Is.EqualTo("sk_existing_fixture"));
            AssertBackup(ClientKey(_config, "Dev"), "pk_existing_fixture");
            AssertBackup(ServerKey(_config, "Dev"), "sk_existing_fixture");
        }

        [Test]
        public void ProcessOverrides_DoNotReplaceLocalBackups()
        {
            _config.SetClientToken("pk_local_fixture");
            PlayServDeploymentSettings.SetLocal(_config, "Dev", "sk_local_fixture");
            Environment.SetEnvironmentVariable("PLAYSERV_ENVIRONMENT", "Dev");
            Environment.SetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN", "pk_process_fixture");
            Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", "sk_process_fixture");
            Assert.That(_config.ClientToken, Is.EqualTo("pk_process_fixture"));
            Assert.That(DeploymentTarget.Read(_config).Key, Is.EqualTo("sk_process_fixture"));
            _config.SetClientToken("pk_attempted_edit_fixture");
            AssertBackup(ClientKey(_config, "Dev"), "pk_local_fixture");
            AssertBackup(ServerKey(_config, "Dev"), "sk_local_fixture");
        }

        [Test]
        public void ExistingPreferenceWinsOverStaleOrDamagedBackup()
        {
            _config.SetClientToken("pk_old_fixture");
            var key = ClientKey(_config, "Dev");
            EditorPrefs.SetString(key, "pk_newer_fixture");
            Assert.That(_config.ClientToken, Is.EqualTo("pk_newer_fixture"));
            AssertBackup(key, "pk_newer_fixture");
            File.WriteAllText(BackupPath(key), "damaged JSON");
            Assert.That(_config.ClientToken, Is.EqualTo("pk_newer_fixture"));
            AssertBackup(key, "pk_newer_fixture");
        }

        [Test]
        public void DamagedBackupWithoutPreferences_DoesNotBlockEnteringANewClientToken()
        {
            _config.SetClientToken("pk_old_fixture");
            var key = ClientKey(_config, "Dev");
            File.WriteAllText(BackupPath(key), "damaged JSON");
            EditorPrefs.DeleteKey(key);
            EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(_config));
            Assert.That(_config.ClientToken, Is.Empty);
            Assert.That(PlayServLocalTokenStore.Instance.Error(key), Is.Not.Empty);
            _config.SetClientToken("pk_reentered_fixture");
            Assert.That(_config.ClientToken, Is.EqualTo("pk_reentered_fixture"));
            AssertBackup(key, "pk_reentered_fixture");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BackupForDifferentProjectOrUnknownVersion_IsNotRestored(bool unknownVersion)
        {
            PlayServDeploymentSettings.SetLocal(_config, "Dev", "sk_original_fixture");
            var key = ServerKey(_config, "Dev");
            File.WriteAllText(BackupPath(key), JsonUtility.ToJson(new BackupRecord {
                schemaVersion = unknownVersion ? 999 : 1,
                key = unknownVersion ? key : PlayServDeploymentSettings.PreferenceKey("another-project", "config", "Dev"),
                value = "sk_foreign_fixture"
            }));
            EditorPrefs.DeleteKey(key);
            Assert.That(PlayServDeploymentSettings.GetLocal(_config, "Dev"), Is.Empty);
            Assert.That(EditorPrefs.HasKey(key), Is.False);
            Assert.That(PlayServLocalTokenStore.Instance.Error(key), Does.Not.Contain("sk_foreign_fixture"));
        }

        [TestCase("")]
        [TestCase("sk_new_fixture")]
        public void FailedBackupWrite_PreservesThePreviousTokenAndReportsNoSecret(string edit)
        {
            var key = ServerKey(_config, "Dev");
            PlayServDeploymentSettings.SetLocal(_config, "Dev", "sk_saved_fixture");
            var original = File.ReadAllText(BackupPath(key));
            var obstruction = Path.GetTempFileName();
            try
            {
                var store = new PlayServLocalTokenStore(Path.Combine(obstruction, "Tokens"));
                Assert.That(store.TrySet(key, edit), Is.False);
                Assert.That(EditorPrefs.GetString(key), Is.EqualTo("sk_saved_fixture"));
                Assert.That(File.ReadAllText(BackupPath(key)), Is.EqualTo(original));
                Assert.That(store.Error(key), Does.Contain("not saved").And.Not.Contain("sk_"));
            }
            finally { File.Delete(obstruction); }
        }

        [Test]
        public void FailedEditWarning_RemainsVisibleUntilAnEditSucceeds()
        {
            var key = ServerKey(_config, "Dev");
            EditorPrefs.SetString(key, "sk_saved_fixture");
            var path = Path.GetTempFileName();
            var store = new PlayServLocalTokenStore(path);
            try
            {
                Assert.That(store.TrySet(key, "sk_failed_fixture"), Is.False);
                File.Delete(path);
                Assert.That(store.TryGet(key, out var saved), Is.True);
                Assert.That(saved, Is.EqualTo("sk_saved_fixture"));
                Assert.That(store.Error(key), Is.Not.Null, "Reading the saved token must not hide a failed edit.");
                Assert.That(store.Error(key), Does.Contain("not saved"));
                Assert.That(store.TrySet(key, "sk_retry_fixture"), Is.True);
                Assert.That(store.Error(key), Is.Null);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacySerializedToken_WithDamagedBackupStillAllowsReadAndReplacement(bool processOverride)
        {
            var key = ClientKey(_config, "Dev");
            _config.SetClientToken("pk_prior_fixture");
            SetLegacyToken("pk_legacy_fixture");
            EditorPrefs.DeleteKey(key);
            EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(_config));
            File.WriteAllText(BackupPath(key), "damaged JSON");
            if (processOverride)
            {
                Environment.SetEnvironmentVariable("PLAYSERV_ENVIRONMENT", "Dev");
                Environment.SetEnvironmentVariable("PLAYSERV_CLIENT_TOKEN", "pk_process_fixture");
            }
            Assert.That(_config.ClientToken, Is.EqualTo(processOverride ? "pk_process_fixture" : "pk_legacy_fixture"));
            if (!processOverride)
            {
                _config.SetClientToken("pk_replacement_fixture");
                Assert.That(_config.ClientToken, Is.EqualTo("pk_replacement_fixture"));
                AssertBackup(key, "pk_replacement_fixture");
                Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
            }
            else AssertBackup(key, "pk_legacy_fixture");
        }

        [Test]
        public void LegacySerializedToken_WithUnwritableBackupRemainsUsableUntilStorageRecovers()
        {
            var key = ClientKey(_config, "Dev");
            SetLegacyToken("pk_legacy_fixture");
            Directory.CreateDirectory(BackupPath(key));
            try
            {
                Assert.That(_config.ClientToken, Is.EqualTo("pk_legacy_fixture"));
                _config.SetClientToken("pk_not_saved_fixture");
                Assert.That(_config.ClientToken, Is.EqualTo("pk_legacy_fixture"));
                Assert.That(PlayServLocalTokenStore.Instance.Error(key), Is.Not.Null);
                Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.EqualTo("pk_legacy_fixture"));
            }
            finally { Directory.Delete(BackupPath(key)); }
            _config.SetClientToken("pk_recovered_fixture");
            Assert.That(_config.ClientToken, Is.EqualTo("pk_recovered_fixture"));
            AssertBackup(key, "pk_recovered_fixture");
            Assert.That(new SerializedObject(_config).FindProperty("clientToken").stringValue, Is.Empty);
        }

        [Test]
        public void CompletedMigration_DoesNotReuseRestoredSerializedTokenInAnotherEnvironment()
        {
            _config.SetClientToken("pk_dev_fixture");
            PlayServEnvironmentClientTokens.SelectEnvironment(_config, "Prod");
            SetLegacyToken("pk_stale_dev_fixture");
            Assert.That(_config.ClientToken, Is.Empty);
            Assert.That(EditorPrefs.HasKey(ClientKey(_config, "Prod")), Is.False);
            Assert.That(File.Exists(BackupPath(ClientKey(_config, "Prod"))), Is.False);
        }

        private void SetLegacyToken(string value)
        {
            var serialized = new SerializedObject(_config);
            serialized.FindProperty("clientToken").stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(_config);
        }

        private PlayServConfig CreateConfig(string name)
        {
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            config.name = name;
            AssetDatabase.CreateAsset(config, _folder + "/" + name + ".asset");
            return config;
        }

        private static void Select(string environment) => EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, environment);
        private static string ClientKey(PlayServConfig config, string environment) => PlayServEnvironmentClientTokens.PreferenceKey(config, environment);
        private static string ServerKey(PlayServConfig config, string environment) => PlayServDeploymentSettings.PreferenceKey(config, environment);
        private static string BackupPath(string key) => Path.GetFullPath(Path.Combine("Library", "PlayServ", "Tokens", Hash128.Compute(key) + ".json"));

        private static void AssertBackup(string key, string value)
        {
            Assert.That(File.Exists(BackupPath(key)), Is.True, "An edit must write the Library backup immediately.");
            var record = JsonUtility.FromJson<BackupRecord>(File.ReadAllText(BackupPath(key)));
            Assert.That(record.schemaVersion, Is.EqualTo(1));
            Assert.That(record.key, Is.EqualTo(key));
            Assert.That(record.value, Is.EqualTo(value));
        }

        [Serializable]
        private sealed class BackupRecord
        {
            public int schemaVersion;
            public string key;
            public string value;
        }
    }
}
