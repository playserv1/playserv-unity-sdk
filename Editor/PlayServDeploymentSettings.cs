using System;
using Playserv.Wrapper;
using UnityEditor;

namespace Playserv.Editor
{
    internal static class PlayServDeploymentSettings
    {
        private static string LegacyPrefix => "PlayServ.PlatformFunctions." + PlayServEnvironmentClientTokens.ProjectScope + ".";
        internal static string LegacyApi { get => EditorPrefs.GetString(LegacyPrefix + "Api", ""); set => EditorPrefs.SetString(LegacyPrefix + "Api", value); }
        internal static string LegacyKey { get => EditorPrefs.GetString(LegacyPrefix + "Key", ""); set { if (string.IsNullOrWhiteSpace(value)) EditorPrefs.DeleteKey(LegacyPrefix + "Key"); else EditorPrefs.SetString(LegacyPrefix + "Key", value.Trim()); } }
        internal static string EnvironmentKey => System.Environment.GetEnvironmentVariable("PLAYSERV_API_KEY");
        internal const string DevDashboard = "https://dashboard.dev.playserv.com";
        internal const string ProdDashboard = "https://dashboard.playserv.com";
        internal static string MigrationKey => "PlayServ.ServerToken." + PlayServEnvironmentClientTokens.ProjectScope + ".migration.v1";

        internal static string PreferenceKey(string project, string guid, string environment) =>
            "PlayServ.ServerToken." + project + "." + guid + "." + environment.ToLowerInvariant();
        internal static string PreferenceKey(PlayServConfig config, string environment) => PreferenceKey(
            PlayServEnvironmentClientTokens.ProjectScope, ConfigId(config), environment);

        internal static string ConfigId(PlayServConfig config)
        {
            var path = config == null ? "" : AssetDatabase.GetAssetPath(config);
            return path.StartsWith("Assets/", StringComparison.Ordinal) ? AssetDatabase.AssetPathToGUID(path) : "";
        }

        internal static string GetLocal(PlayServConfig config, string environment) =>
            ConfigId(config).Length == 0 ? "" : EditorPrefs.GetString(PreferenceKey(config, environment), "");

        internal static void SetLocal(PlayServConfig config, string environment, string value)
        {
            if (ConfigId(config).Length == 0) throw new InvalidOperationException("Save the PlayServ Config in Assets before storing a Server Token.");
            var key = PreferenceKey(config, environment);
            if (string.IsNullOrWhiteSpace(value)) EditorPrefs.DeleteKey(key);
            else EditorPrefs.SetString(key, value.Trim());
        }

        internal static string MigrateLegacyKey(PlayServConfig config)
        {
            if (ConfigId(config).Length == 0 || EditorPrefs.GetBool(MigrationKey, false) || string.IsNullOrWhiteSpace(LegacyKey)) return null;
            var environment = EnvironmentForDashboard(LegacyApi);
            if (environment == null) return "The old Deployment key has an unknown environment. It was preserved locally. Enter a Server Token separately for Dev and Prod.";
            var key = PreferenceKey(config, environment);
            if (!EditorPrefs.HasKey(key)) SetLocal(config, environment, LegacyKey);
            EditorPrefs.SetBool(MigrationKey, true);
            LegacyKey = "";
            return null;
        }

        internal static string EnvironmentForDashboard(string address)
        {
            var value = (address ?? "").Trim().TrimEnd('/');
            if (string.Equals(value, DevDashboard, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "https://dashboard.dev.playserv.io", StringComparison.OrdinalIgnoreCase)) return "Dev";
            if (string.Equals(value, ProdDashboard, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "https://dashboard.playserv.io", StringComparison.OrdinalIgnoreCase)) return "Prod";
            return null;
        }

        internal static string CurrentDashboard(string address)
        {
            var value = (address ?? "").Trim().TrimEnd('/');
            if (string.Equals(value, "https://dashboard.dev.playserv.io", StringComparison.OrdinalIgnoreCase)) return DevDashboard;
            if (string.Equals(value, "https://dashboard.playserv.io", StringComparison.OrdinalIgnoreCase)) return ProdDashboard;
            return address;
        }

        internal static void MigrateDashboard(PlayServConfig config)
        {
            if (config == null) return;
            var address = CurrentDashboard(config.DashboardAddress);
            if (address == config.DashboardAddress) return;
            var serialized = new SerializedObject(config);
            serialized.FindProperty("dashboardAddress").stringValue = address;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            if (AssetDatabase.Contains(config)) AssetDatabase.SaveAssetIfDirty(config);
        }
    }

}
