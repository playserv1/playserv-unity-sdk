#if UNITY_EDITOR
using Playserv.Examples;
using UnityEditor;

namespace Playserv.Editor
{
    [CustomEditor(typeof(PlayServBootstrapSample))]
    internal sealed class PlayServBootstrapSampleInspector : UnityEditor.Editor
    {
        private static readonly string[] ReadOnlyEndpointPropertyOrder =
        {
            "backendServerAddress",
            "deployApiServerAddress",
            "schemaApiServerAddress"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var overrideProperty = serializedObject.FindProperty("overrideCredentialsFromInspector");
            var useInspectorCredentials = overrideProperty != null && overrideProperty.boolValue;

            DrawOverrideProperty(overrideProperty);
            DrawCredentialProperties(useInspectorCredentials);
            DrawBehaviorProperties();

            EditorGUILayout.Space(4);
            DrawProperties(ReadOnlyEndpointPropertyOrder, readOnly: true);

            if (serializedObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(target);
        }

        private void DrawOverrideProperty(SerializedProperty property)
        {
            if (property == null)
                return;

            EditorGUILayout.PropertyField(property, includeChildren: true);
        }

        private void DrawCredentialProperties(bool useInspectorCredentials)
        {
            var credentialProperties = new[]
            {
                "clientToken",
                "authorization",
                "gameId",
                "userId",
                "gameVersion"
            };

            using (new EditorGUI.DisabledScope(!useInspectorCredentials))
            {
                DrawProperties(credentialProperties, readOnly: false);
            }
        }

        private void DrawBehaviorProperties()
        {
            var behaviorProperties = new[]
            {
                "autoConnect",
                "disconnectOnDestroy",
                "keepAlivePingIntervalMs",
                "keepAlivePongTimeoutMs"
            };

            DrawProperties(behaviorProperties, readOnly: false);
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
