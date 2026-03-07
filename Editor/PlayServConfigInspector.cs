#if UNITY_EDITOR
using System;
using System.Reflection;
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
            "allowMultipleConnections",
            "keepAlivePingIntervalMs",
            "keepAlivePongTimeoutMs",
            "networkTransformSyncIntervalMs",
            "deployAuthToken",
            "timeoutSeconds"
        };

        private static readonly string[] SdkVersionPropertyOrder =
        {
            "sdkVersion"
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
            DrawProperties(SdkVersionPropertyOrder, readOnly: !CanEditSdkVersionInClientEditor());

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

        private static bool CanEditSdkVersionInClientEditor()
        {
            var managerType = FindClientEnvironmentManagerType();
            if (managerType == null)
                return false;

            var setMethod = managerType.GetMethod(
                "TrySetActiveEnvironment",
                BindingFlags.Static | BindingFlags.NonPublic);

            return setMethod != null;
        }

        private static Type FindClientEnvironmentManagerType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("Playserv.ClientEditor.PlayServEnvironmentManager", throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
#endif
