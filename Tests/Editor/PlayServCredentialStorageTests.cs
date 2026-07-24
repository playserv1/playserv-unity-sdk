using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServCredentialStorageTests
    {
        private string _originalEnvironmentToken;

        [SetUp]
        public void SetUp()
        {
            _originalEnvironmentToken = Environment.GetEnvironmentVariable(
                PlayServDeployCredentialStore.EnvironmentVariableName);
            Environment.SetEnvironmentVariable(
                PlayServDeployCredentialStore.EnvironmentVariableName,
                null);
            PlayServDeployCredentialStore.ClearLocalToken();
        }

        [TearDown]
        public void TearDown()
        {
            PlayServDeployCredentialStore.ClearLocalToken();
            Environment.SetEnvironmentVariable(
                PlayServDeployCredentialStore.EnvironmentVariableName,
                _originalEnvironmentToken);
        }

        [Test]
        public void ResolveToken_EnvironmentOverridesLocalStorage()
        {
            PlayServDeployCredentialStore.SetLocalToken("sk_local");
            Environment.SetEnvironmentVariable(
                PlayServDeployCredentialStore.EnvironmentVariableName,
                "Bearer sk_ci");

            Assert.That(PlayServDeployCredentialStore.ResolveToken(), Is.EqualTo("sk_ci"));
        }

        [Test]
        public void MigrateLegacySecrets_MovesDeployTokenAndClearsConfigFields()
        {
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            try
            {
                var serialized = new SerializedObject(config);
                serialized.FindProperty("legacyAuthorizationForMigration").stringValue = "Bearer sk_runtime";
                serialized.FindProperty("legacyDeployAuthTokenForMigration").stringValue = "sk_deploy";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                LogAssert.Expect(
                    LogType.Log,
                    "[PlayServ] Moved the legacy deploy token from PlayServConfig to project-scoped local Editor storage.");
                LogAssert.Expect(
                    LogType.Warning,
                    new Regex("Removed serialized runtime authorization.*revoke and rotate it"));

                Assert.That(PlayServDeployCredentialStore.MigrateLegacySecrets(config), Is.True);
                Assert.That(PlayServDeployCredentialStore.GetLocalToken(), Is.EqualTo("sk_deploy"));

                serialized.Update();
                Assert.That(
                    serialized.FindProperty("legacyAuthorizationForMigration").stringValue,
                    Is.Empty);
                Assert.That(
                    serialized.FindProperty("legacyDeployAuthTokenForMigration").stringValue,
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }
}
