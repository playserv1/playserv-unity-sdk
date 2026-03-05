#if UNITY_EDITOR
using Playserv.Samples;
using UnityEditor;

namespace Playserv.Editor
{
    [CustomEditor(typeof(PlayServBootstrapSample))]
    internal sealed class PlayServBootstrapSampleInspector : UnityEditor.Editor
    {
        private static readonly string[] EditablePropertyOrder =
        {
            "gameAccessToken",
            "gameId",
            "userId",
            "gameVersion",
            "autoConnect",
            "disconnectOnDestroy",
            "keepAlivePingIntervalMs",
            "keepAlivePongTimeoutMs"
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
            DrawProperties(ReadOnlyEndpointPropertyOrder, readOnly: true);

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
