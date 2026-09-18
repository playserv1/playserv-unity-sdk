using System;
using System.IO;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal sealed class PlayServEnvironmentClientTokens : PlayServConfig.IEditorClientTokens
    {
        internal const string EnvironmentVariable = "PLAYSERV_ENVIRONMENT";
        internal const string TokenVariable = "PLAYSERV_CLIENT_TOKEN";
        internal const string LegacyEnvironmentPreference = "PlayServ.PackageEnvironment.Active";
        internal static string ProjectScope => Hash128.Compute(Path.GetFullPath(Application.dataPath)).ToString();
        internal static string ActiveEnvironmentPreference => "PlayServ.Environment." + ProjectScope;

        static PlayServEnvironmentClientTokens()
        {
            PlayServConfig.EditorClientTokens = new PlayServEnvironmentClientTokens();
        }

        internal static bool IsManaged(PlayServConfig config)
        {
            if (config == null || PlayServClientProjectSettingsRegistry.HasProvider)
                return false;
            return AssetDatabase.GetAssetPath(config).StartsWith("Assets/", StringComparison.Ordinal) &&
                   !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(config)));
        }

        internal static string LocalEnvironment
        {
            get
            {
                if (!EditorPrefs.HasKey(ActiveEnvironmentPreference))
                {
                    var legacy = EditorPrefs.GetString(LegacyEnvironmentPreference, "Dev");
                    EditorPrefs.SetString(ActiveEnvironmentPreference, AvailableEnvironment(legacy));
                }
                var stored = EditorPrefs.GetString(ActiveEnvironmentPreference);
                var available = AvailableEnvironment(stored);
                if (stored != available)
                    EditorPrefs.SetString(ActiveEnvironmentPreference, available);
                return available;
            }
        }

        internal static string ActiveEnvironment => TryGetProcessOverride(out var environment, out _)
            ? environment : LocalEnvironment;

        internal static void SelectEnvironment(PlayServConfig config, string environment)
        {
            if (TryGetProcessOverride(out _, out _))
                return;
            // Selection is project-wide, so unread configs must retain the old association too.
            foreach (var guid in AssetDatabase.FindAssets("t:PlayServConfig", new[] { "Assets" }))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<PlayServConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (IsManaged(candidate))
                    EnsureMigrated(candidate);
            }
            EditorPrefs.SetString(ActiveEnvironmentPreference, NormalizeEnvironment(environment));
        }

        internal static string PreferenceKey(string projectScope, string guid, string environment)
        {
            return "PlayServ.ClientToken." + projectScope + "." + guid + "." +
                   Hash128.Compute(NormalizeEnvironment(environment).ToLowerInvariant());
        }

        internal static string PreferenceKey(PlayServConfig config, string environment) => PreferenceKey(
            ProjectScope, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(config)), environment);

        internal static string MigrationKey(PlayServConfig config) => PreferenceKey(config, "migration") + ".v1";

        internal static void EnsureMigrated(PlayServConfig config)
        {
            var marker = MigrationKey(config);
            if (EditorPrefs.GetBool(marker, false))
                return;

            var key = PreferenceKey(config, LocalEnvironment);
            if (!EditorPrefs.HasKey(key))
                EditorPrefs.SetString(key, config.SerializedClientToken ?? string.Empty);
            // Do not mark migration complete until the on-disk value is cleared.
            config.SetSerializedClientToken(string.Empty);
            AssetDatabase.SaveAssetIfDirty(config);
            if (EditorUtility.IsDirty(config))
                throw new IOException("PlayServ config could not be saved; client token migration will be retried.");
            EditorPrefs.SetBool(marker, true);
        }

        public bool TryGet(PlayServConfig config, out string value)
        {
            value = null;
            if (!IsManaged(config))
                return false;
            EnsureMigrated(config);
            value = TryGetProcessOverride(out _, out var processToken)
                ? processToken : EditorPrefs.GetString(PreferenceKey(config, LocalEnvironment), string.Empty);
            return true;
        }

        public bool TrySet(PlayServConfig config, string value, out bool changed)
        {
            changed = false;
            if (!IsManaged(config))
                return false;
            EnsureMigrated(config);
            // Process credentials are read-only and never copied to the local store.
            if (TryGetProcessOverride(out _, out _))
                return true;
            var key = PreferenceKey(config, LocalEnvironment);
            value = value ?? string.Empty;
            changed = !EditorPrefs.HasKey(key) || EditorPrefs.GetString(key) != value;
            if (changed)
                EditorPrefs.SetString(key, value);
            return true;
        }

        internal static bool TryGetProcessOverride(out string environment, out string token)
        {
            environment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            token = Environment.GetEnvironmentVariable(TokenVariable);
            if (environment == null && token == null)
                return false;
            if (string.IsNullOrWhiteSpace(environment) || string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "Set both PLAYSERV_ENVIRONMENT and PLAYSERV_CLIENT_TOKEN, or neither.");
            environment = NormalizeEnvironment(environment);
            if (!PlayServPackageEnvironmentProvider.IsKnownEnvironment(environment))
                throw new InvalidOperationException("PLAYSERV_ENVIRONMENT must name an available PlayServ environment.");
            token = PlayServCredentialPolicy.NormalizeClientToken(token);
            return true;
        }

        private static string NormalizeEnvironment(string value)
        {
            if (!PlayServPackageEnvironmentProvider.TryNormalizeEnvironmentName(value, out var normalized))
                return "Dev";
            return normalized;
        }

        private static string AvailableEnvironment(string value)
        {
            var normalized = NormalizeEnvironment(value);
            return PlayServPackageEnvironmentProvider.IsKnownEnvironment(normalized) ? normalized : "Dev";
        }

        internal static void DrawTokenField(PlayServConfig config)
        {
            var processOverride = TryGetProcessOverride(out _, out _);
            using (new EditorGUI.DisabledScope(processOverride))
            {
                EditorGUI.BeginChangeCheck();
                var value = EditorGUILayout.TextField("Client Token (" + ActiveEnvironment + ")", config.ClientToken);
                if (EditorGUI.EndChangeCheck())
                    config.SetClientToken(value);
            }
            EditorGUILayout.HelpBox(processOverride
                ? "Client Token is supplied by process environment variables (read-only)."
                : "Client Token is stored locally for this project, config and environment, not in the config asset. " +
                  "Reconfigure the SDK to apply changes to an active session.", MessageType.Info);
        }
    }
}
