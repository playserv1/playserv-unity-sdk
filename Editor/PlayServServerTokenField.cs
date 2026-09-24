using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServServerTokenField
    {
        private string _scope, _draft, _saved;
        internal void Draw(PlayServConfig config)
        {
            var environment = PlayServEnvironmentClientTokens.ActiveEnvironment;
            var notice = PlayServDeploymentSettings.MigrateLegacyKey(config);
            var scope = PlayServDeploymentSettings.PreferenceKey(config, environment);
            var saved = PlayServDeploymentSettings.GetLocal(config, environment);
            if (_scope != scope || _saved != saved) { _scope = scope; _saved = saved; _draft = saved; }
            var overridden = !string.IsNullOrWhiteSpace(PlayServDeploymentSettings.EnvironmentKey);
            using (new EditorGUI.DisabledScope(overridden || PlayServDeploymentSettings.ConfigId(config).Length == 0))
            {
                if (overridden) EditorGUILayout.PasswordField("Server Token (" + environment + ")", "environment");
                else _draft = EditorGUILayout.PasswordField("Server Token (" + environment + ")", _draft);
                if (!overridden)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth);
                    if (GUILayout.Button("Save locally", GUILayout.Width(100))) { PlayServDeploymentSettings.SetLocal(config, environment, _draft); _saved = _draft.Trim(); }
                    if (GUILayout.Button("Clear", GUILayout.Width(60))) { PlayServDeploymentSettings.SetLocal(config, environment, ""); _saved = _draft = ""; }
                }
            }
            EditorGUILayout.HelpBox(overridden ? "Server Token is supplied by PLAYSERV_API_KEY (read-only)." :
                "Server Token is stored only on this computer for this project, config and environment. It is never included in a player build.", MessageType.Info);
            if (notice != null) EditorGUILayout.HelpBox(notice, MessageType.Warning);
        }
    }
}
