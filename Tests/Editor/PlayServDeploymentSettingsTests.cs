using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Editor.Tests
{
    public sealed class PlayServDeploymentSettingsTests
    {
        private string _folder, _legacyApi, _legacyKey, _environmentKey, _environment;
        private bool _migrated;
        private PlayServConfig _first, _second;
        [SetUp] public void Setup()
        {
            _legacyApi = PlatformFunctionEditorStore.Api; _legacyKey = PlatformFunctionEditorStore.LocalKey;
            _migrated = EditorPrefs.GetBool(PlayServDeploymentSettings.MigrationKey);
            _environmentKey = System.Environment.GetEnvironmentVariable("PLAYSERV_API_KEY");
            _environment = EditorPrefs.GetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, "Dev");
            System.Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", null);
            PlatformFunctionEditorStore.LocalKey = ""; EditorPrefs.DeleteKey(PlayServDeploymentSettings.MigrationKey);
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, "Dev");
            _folder = "Assets/DeploymentFixture" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_folder));
            _first = Create("First"); _second = Create("Second");
        }
        private PlayServConfig Create(string name)
        {
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            AssetDatabase.CreateAsset(config, _folder + "/" + name + ".asset");
            return config;
        }
        [TearDown] public void Cleanup()
        {
            foreach (var config in new[] { _first, _second })
                {
                    foreach (var env in new[] { "Dev", "Prod" })
                    {
                        EditorPrefs.DeleteKey(PlayServDeploymentSettings.PreferenceKey(config, env));
                        EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.PreferenceKey(config, env));
                    }
                    EditorPrefs.DeleteKey(PlayServEnvironmentClientTokens.MigrationKey(config));
                }
            AssetDatabase.DeleteAsset(_folder);
            PlatformFunctionEditorStore.Api = _legacyApi; PlatformFunctionEditorStore.LocalKey = _legacyKey;
            EditorPrefs.SetBool(PlayServDeploymentSettings.MigrationKey, _migrated);
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, _environment);
            System.Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", _environmentKey);
        }
        [Test] public void ServerTokenIsIsolatedByProjectConfigAndEnvironmentAndNeverSerialized()
        {
            PlayServDeploymentSettings.SetLocal(_first, "Dev", "sk_fixture_dev");
            PlayServDeploymentSettings.SetLocal(_first, "Prod", "sk_fixture_prod");
            Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Dev"), Is.EqualTo("sk_fixture_dev"));
            Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Prod"), Is.EqualTo("sk_fixture_prod"));
            Assert.That(PlayServDeploymentSettings.GetLocal(_second, "Dev"), Is.Empty);
            Assert.That(PlayServDeploymentSettings.PreferenceKey("project-a", "config", "Dev"), Is.Not.EqualTo(PlayServDeploymentSettings.PreferenceKey("project-b", "config", "Dev")));
            AssetDatabase.SaveAssets();
            Assert.That(File.ReadAllText(AssetDatabase.GetAssetPath(_first)), Does.Not.Contain("sk_fixture"));
            Assert.That(JsonUtility.ToJson(_first.ToSettings()), Does.Not.Contain("sk_fixture"));
            PlayServDeploymentSettings.SetLocal(_first, "Dev", "");
            Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Dev"), Is.Empty);
        }
        [Test] public void ProcessTokenOverridesLocalWithoutCopyingIt()
        {
            PlayServDeploymentSettings.SetLocal(_first, "Dev", "sk_local");
            System.Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", "sk_process");
            Assert.That(DeploymentTarget.Read(_first).Key, Is.EqualTo("sk_process"));
            Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Dev"), Is.EqualTo("sk_local"));
        }
        [TestCase("https://dashboard.dev.playserv.io/", "Dev")]
        [TestCase("https://dashboard.playserv.com", "Prod")]
        public void LegacyTokenMigratesOnceToTheEnvironmentOfItsSavedDeploymentAddress(string api, string env)
        {
            PlatformFunctionEditorStore.Api = api; PlatformFunctionEditorStore.LocalKey = "sk_legacy";
            Assert.That(PlayServDeploymentSettings.MigrateLegacyKey(_first), Is.Null);
            Assert.That(PlayServDeploymentSettings.GetLocal(_first, env), Is.EqualTo("sk_legacy"));
            Assert.That(PlatformFunctionEditorStore.LocalKey, Is.Empty);
            PlayServDeploymentSettings.MigrateLegacyKey(_second);
            Assert.That(PlayServDeploymentSettings.GetLocal(_second, env), Is.Empty);
        }
        [Test] public void UnknownLegacyEnvironmentIsPreservedForManualRecovery()
        {
            PlatformFunctionEditorStore.Api = "https://custom.example"; PlatformFunctionEditorStore.LocalKey = "sk_legacy";
            Assert.That(PlayServDeploymentSettings.MigrateLegacyKey(_first), Does.Contain("unknown environment"));
            Assert.That(PlatformFunctionEditorStore.LocalKey, Is.EqualTo("sk_legacy"));
            Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Dev"), Is.Empty);
            Assert.That(EditorPrefs.GetBool(PlayServDeploymentSettings.MigrationKey), Is.False);
        }
        [TestCase("https://dashboard.dev.playserv.io/", "https://dashboard.dev.playserv.com")]
        [TestCase("https://dashboard.playserv.io", "https://dashboard.playserv.com")]
        [TestCase("https://custom.example/dashboard", "https://custom.example/dashboard")]
        public void DashboardMigrationChangesOnlyKnownOldDefaults(string before, string after)
        {
            var serialized = new SerializedObject(_first);
            serialized.FindProperty("dashboardAddress").stringValue = before; serialized.ApplyModifiedPropertiesWithoutUndo();
            PlayServDeploymentSettings.MigrateDashboard(_first);
            Assert.That(_first.DashboardAddress, Is.EqualTo(after));
            Assert.That(DeploymentTarget.Read(_first).Api, Is.EqualTo(after));
        }
        [Test] public void EveryTargetIdentityChangeInvalidatesThePreviousSnapshot()
        {
            var target = DeploymentTarget.Read(_first);
            Assert.That(target.Equals(DeploymentTarget.Read(_first)), Is.True);
            Assert.That(target.Equals(DeploymentTarget.Read(_second)), Is.False);
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, "Prod");
            Assert.That(target.Equals(DeploymentTarget.Read(_first)), Is.False);
            EditorPrefs.SetString(PlayServEnvironmentClientTokens.ActiveEnvironmentPreference, "Dev");
            PlayServDeploymentSettings.SetLocal(_first, "Dev", "sk_new");
            Assert.That(target.Equals(DeploymentTarget.Read(_first)), Is.False);
            target = DeploymentTarget.Read(_first);
            var serialized = new SerializedObject(_first); serialized.FindProperty("dashboardAddress").stringValue = "https://changed.example"; serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(target.Equals(DeploymentTarget.Read(_first)), Is.False);
        }
        private sealed class AuthHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fixture\",\"project_slug\":\"test\",\"env\":\"prod\"}") });
        }
        [Test] public void ServerTokenForAnotherEnvironmentCannotAuthorizeDeployment()
        {
            using (var client = new DeploymentTarget("fixture", "Dev", "https://platform.example", "sk_fixture").CreateClient(new HttpClient(new AuthHandler())))
            {
                Assert.Throws<InvalidOperationException>(() => Task.Run(() => client.ConnectAsync(default)).GetAwaiter().GetResult());
                Assert.Throws<InvalidOperationException>(() => Task.Run(() => client.ListGameServersAsync(default)).GetAwaiter().GetResult());
            }
        }

        [UnityTest] public IEnumerator SettingsAndInspectorRenderScopedTokensAtBothWidths()
        {
            PlayServDeploymentSettings.SetLocal(_first, "Dev", "sk_masked_ui_fixture");
            var window = ScriptableObject.CreateInstance<DeploymentTokenTestWindow>();
            try
            {
                window.Config = _first; window.ShowUtility();
                foreach (var width in new[] { 420, 1000 })
                foreach (var inspector in new[] { false, true })
                {
                    window.position = new Rect(150, 100, width, 850);
                    window.Inspector = inspector; window.Rendered = false;
                    for (var i = 0; i < 30 && !window.Rendered; i++) { window.Repaint(); yield return null; }
                    Assert.That(window.Rendered, Is.True);
                    var capture = System.Environment.GetEnvironmentVariable("PLAYSERV_IMAGE_UI_CAPTURE");
                    if (string.IsNullOrEmpty(capture)) continue;
                    Directory.CreateDirectory(capture);
                    var scale = EditorGUIUtility.pixelsPerPoint;
                    var texture = new Texture2D((int)(width * scale), (int)(window.position.height * scale));
                    try
                    {
                        texture.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(window.position.position * scale, texture.width, texture.height));
                        texture.Apply(); File.WriteAllBytes(Path.Combine(capture, (inspector ? "inspector-" : "settings-") + width + ".png"), texture.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
            }
            finally { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
        }

        [UnityTest] public IEnumerator ServerTokenEditsSaveImmediatelyAndEmptyTextClearsTheLocalKey()
        {
            var field = new PlayServServerTokenField();
            var window = ScriptableObject.CreateInstance<DeploymentLayoutTestWindow>();
            var clipboard = EditorGUIUtility.systemCopyBuffer;
            try
            {
                PlayServDeploymentSettings.SetLocal(_first, "Dev", "sk_visible_fixture");
                window.DrawPanel = _ => field.Draw(_first);
                window.ShowUtility(); window.position = new Rect(150, 100, 600, 200); window.Focus();
                for (var i = 0; i < 30 && window.Repaints == 0; i++) { window.Repaint(); yield return null; }
                Assert.That(window.Repaints, Is.GreaterThan(0));
                var point = new Vector2(300, 10);
                window.SendEvent(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 });
                window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 });
                window.SendEvent(new Event { type = EventType.ExecuteCommand, commandName = "SelectAll" });
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.X, character = 'x' });
                Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Dev"), Is.EqualTo("x"), "Typing must save without a Save button.");
                window.Repaint(); yield return null;
                window.Focus();
                window.SendEvent(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 });
                window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 });
                window.SendEvent(new Event { type = EventType.ExecuteCommand, commandName = "SelectAll" });
                window.SendEvent(new Event { type = EventType.ExecuteCommand, commandName = "Cut" });
                Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Dev"), Is.Empty);
                Assert.That(EditorPrefs.HasKey(PlayServDeploymentSettings.PreferenceKey(_first, "Dev")), Is.False);
            }
            finally { EditorGUIUtility.systemCopyBuffer = clipboard; window.DrawPanel = null; window.Close(); UnityEngine.Object.DestroyImmediate(window); }
        }

        [UnityTest] public IEnumerator ServerTokenIsVisibleAndSelectableLikeClientToken()
        {
            var field = new PlayServServerTokenField();
            var window = ScriptableObject.CreateInstance<DeploymentLayoutTestWindow>();
            var clipboard = EditorGUIUtility.systemCopyBuffer;
            try
            {
                PlayServDeploymentSettings.SetLocal(_first, "Dev", "sk_visible_fixture");
                window.DrawPanel = _ => field.Draw(_first);
                window.ShowUtility(); window.position = new Rect(150, 100, 600, 200); window.Focus();
                for (var i = 0; i < 30 && window.Repaints == 0; i++) { window.Repaint(); yield return null; }
                var point = new Vector2(300, 10);
                window.SendEvent(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 });
                window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 });
                EditorGUIUtility.systemCopyBuffer = "unchanged";
                window.SendEvent(new Event { type = EventType.ExecuteCommand, commandName = "SelectAll" });
                window.SendEvent(new Event { type = EventType.ExecuteCommand, commandName = "Copy" });
                Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo("sk_visible_fixture"));
            }
            finally { EditorGUIUtility.systemCopyBuffer = clipboard; window.DrawPanel = null; window.Close(); UnityEngine.Object.DestroyImmediate(window); }
        }

        [UnityTest] public IEnumerator ProcessServerTokenCannotBeEditedOrCopiedIntoLocalStorage()
        {
            var field = new PlayServServerTokenField();
            var window = ScriptableObject.CreateInstance<DeploymentLayoutTestWindow>();
            try
            {
                PlayServDeploymentSettings.SetLocal(_first, "Dev", "sk_local_fixture");
                System.Environment.SetEnvironmentVariable("PLAYSERV_API_KEY", "sk_process_fixture");
                window.DrawPanel = _ => field.Draw(_first);
                window.ShowUtility(); window.position = new Rect(150, 100, 600, 200); window.Focus();
                for (var i = 0; i < 30 && window.Repaints == 0; i++) { window.Repaint(); yield return null; }
                window.SendEvent(new Event { type = EventType.MouseDown, mousePosition = new Vector2(300, 10), button = 0 });
                window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = new Vector2(300, 10), button = 0 });
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.X, character = 'x' });
                Assert.That(DeploymentTarget.Read(_first).Key, Is.EqualTo("sk_process_fixture"));
                Assert.That(PlayServDeploymentSettings.GetLocal(_first, "Dev"), Is.EqualTo("sk_local_fixture"));
            }
            finally { window.DrawPanel = null; window.Close(); UnityEngine.Object.DestroyImmediate(window); }
        }
    }

    internal sealed class DeploymentTokenTestWindow : EditorWindow
    {
        internal PlayServConfig Config;
        internal bool Inspector, Rendered;
        private UnityEditor.Editor _inspector;
        private PlayServWindowContext _context;
        private readonly PlayServConfigSectionPresenter _presenter = new PlayServConfigSectionPresenter();
        private PlayServConnectionController _connection;
        private void OnGUI()
        {
            if (_inspector == null) _inspector = UnityEditor.Editor.CreateEditor(Config);
            if (_context == null)
            {
                var serialized = new SerializedObject(Config);
                var state = new PlayServWindowState
                {
                    Config = Config, SerializedObject = serialized, FoldConfig = true,
                    ClientTokenProperty = serialized.FindProperty("clientToken"),
                    DeploymentGameIdProperty = serialized.FindProperty("deploymentGameId"),
                    GameVersionProperty = serialized.FindProperty("gameVersion"),
                    AllowMultipleConnectionsProperty = serialized.FindProperty("allowMultipleConnections")
                };
                _connection = new PlayServConnectionController(state, Repaint);
                _context = new PlayServWindowContext(this, state, _connection, "", 25, () => { }, () => { }, () => { }, Repaint);
            }
            PlayServWindowTheme.Ensure();
            if (Inspector) _inspector.OnInspectorGUI(); else _presenter.Draw(_context);
            if (Event.current.type == EventType.Repaint) Rendered = true;
        }
        private void OnDisable() { if (_inspector != null) DestroyImmediate(_inspector); _connection?.Dispose(); }
    }
}
