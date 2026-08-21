#if UNITY_EDITOR
using Playserv.DebugTerminal;
using UnityEditor;

namespace Playserv.DebugTerminal.Editor
{
    [CustomEditor(typeof(PlayServDebugTerminalBootstrap))]
    internal sealed class PlayServDebugTerminalBootstrapInspector : UnityEditor.Editor
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

            DrawBehaviorProperties();

            EditorGUILayout.Space(4);
            DrawProperties(ReadOnlyEndpointPropertyOrder, readOnly: true);

            if (serializedObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(target);
        }

        private void DrawBehaviorProperties()
        {
            var behaviorProperties = new[]
            {
                "showStartupConfigurationPopup",
                "autoConnect",
                "disconnectOnDestroy"
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
