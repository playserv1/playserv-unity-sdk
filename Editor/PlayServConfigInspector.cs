#if UNITY_EDITOR
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    [CustomEditor(typeof(PlayServConfig))]
    internal sealed class PlayServConfigInspector : UnityEditor.Editor
    {
        private static readonly string[] EditablePropertyOrder =
        {
            "gameAccessToken",
            "gameId",
            "userId",
            "gameVersion",
            "sdkVersion",
            "allowMultipleConnections",
            "keepAlivePingIntervalMs",
            "keepAlivePongTimeoutMs",
            "networkTransformSyncIntervalMs",
            "deployAuthToken",
            "timeoutSeconds"
        };

        private static readonly string[] ReadOnlyEndpointPropertyOrder =
        {
            "backendServerAddress",
            "deployApiServerAddress",
            "schemaApiServerAddress"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawProperties(EditablePropertyOrder, readOnly: false);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Environment-managed endpoint settings", EditorStyles.boldLabel);
            DrawProperties(ReadOnlyEndpointPropertyOrder, readOnly: true);

            EditorGUILayout.HelpBox(
                $"Edit endpoint values only in {PlayServEnvironmentResolver.ConfigFileRelativePath}.",
                MessageType.Info);

            if (serializedObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(target);
        }

        private void DrawProperties(string[] propertyNames, bool readOnly)
        {
            foreach (var propertyName in propertyNames)
            {
                var property = serializedObject.FindProperty(propertyName);
                if (property == null)
                    continue;

                using (new EditorGUI.DisabledScope(readOnly))
                    EditorGUILayout.PropertyField(property, includeChildren: true);
            }
        }
    }
}
#endif
