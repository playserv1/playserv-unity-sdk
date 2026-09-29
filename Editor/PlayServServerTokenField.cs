using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServServerTokenField
    {
        internal void Draw(PlayServConfig config)
        {
            var environment = PlayServEnvironmentClientTokens.ActiveEnvironment;
            var notice = PlayServDeploymentSettings.MigrateLegacyKey(config);
            var saved = PlayServDeploymentSettings.GetLocal(config, environment);
            var processToken = PlayServDeploymentSettings.EnvironmentKey;
            var overridden = !string.IsNullOrWhiteSpace(processToken);
            using (new EditorGUI.DisabledScope(overridden || PlayServDeploymentSettings.ConfigId(config).Length == 0))
            {
                EditorGUI.BeginChangeCheck();
                var value = EditorGUILayout.TextField("Server Token (" + environment + ")", overridden ? processToken : saved);
                if (EditorGUI.EndChangeCheck() && !overridden)
                    PlayServDeploymentSettings.SetLocal(config, environment, value);
            }
            var error = PlayServLocalTokenStore.Instance.Error(PlayServDeploymentSettings.PreferenceKey(config, environment));
            EditorGUILayout.HelpBox(overridden ? "Server Token is supplied by PLAYSERV_API_KEY (read-only)." :
                error ?? "Server Token saves immediately to local preferences and Library/PlayServ for this project, config and environment. Empty the field to clear it. It is never included in a player build.",
                !overridden && error != null ? MessageType.Warning : MessageType.Info);
            if (notice != null) EditorGUILayout.HelpBox(notice, MessageType.Warning);
        }
    }
}
