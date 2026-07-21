using Playserv.AppleSignIn;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor.AppleSignIn
{
    [InitializeOnLoad]
    internal static class PlayServAppleSignInSettingsSectionRegistration
    {
        static PlayServAppleSignInSettingsSectionRegistration()
        {
            PlayServModuleSettingsSectionRegistry.Register(new PlayServAppleSignInSettingsSection());
        }
    }

    internal sealed class PlayServAppleSignInSettingsSection : IPlayServModuleSettingsSection
    {
        public int Order => 40;

        public bool Draw(
            PlayServWindowContext context,
            PlayServEditorModuleSettings settings,
            bool hasRuntimeModules,
            bool hasServerRuntimeModules)
        {
            if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(PlayServEditorModuleSettings.RuntimeModuleAppleSignIn))
                return false;

            GUILayout.Space(12f);
            GUILayout.Label("Apple Sign In", PlayServWindowTheme.MiniHeadingStyle);
            GUILayout.Space(6f);

            var changed = false;
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardBodyStyle))
            {
                var asset = PlayServAppleSignInSettingsAssetProvider.FindExisting();
                if (!settings.RuntimeAppleSignIn)
                {
                    PlayServWindowChrome.DrawNotice("Enable the Apple Sign In runtime module to use these credentials in builds.", MessageType.Info);
                    GUILayout.Space(6f);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton("Create/Select Settings", PlayServWindowButtonTone.Secondary, GUILayout.Height(28f), GUILayout.Width(166f)))
                    {
                        asset = PlayServAppleSignInSettingsAssetProvider.GetOrCreate();
                        Selection.activeObject = asset;
                        changed = true;
                    }

                    GUILayout.Space(6f);

                    using (new EditorGUI.DisabledScope(asset == null))
                    {
                        if (PlayServWindowChrome.DrawActionButton("Ping", PlayServWindowButtonTone.Ghost, GUILayout.Height(28f), GUILayout.Width(72f)) &&
                            asset != null)
                        {
                            EditorGUIUtility.PingObject(asset);
                        }
                    }

                    GUILayout.FlexibleSpace();
                }

                GUILayout.Space(8f);

                if (asset == null)
                {
                    PlayServWindowChrome.DrawNotice("Create a settings asset to edit Apple credentials.", MessageType.Info);
                    return changed;
                }

                var serializedObject = new SerializedObject(asset);
                serializedObject.Update();

                GUILayout.Label("Scopes", PlayServWindowTheme.MiniHeadingStyle);
                DrawProperty(serializedObject, "requestEmail");
                DrawProperty(serializedObject, "requestFullName");
                DrawProperty(serializedObject, "defaultNonce");
                DrawProperty(serializedObject, "defaultState");

                GUILayout.Space(8f);
                GUILayout.Label("Credentials", PlayServWindowTheme.MiniHeadingStyle);
                DrawProperty(serializedObject, "clientId");
                DrawProperty(serializedObject, "teamId");
                DrawProperty(serializedObject, "serviceId");
                DrawProperty(serializedObject, "keyId");
                DrawProperty(serializedObject, "redirectUri");
                DrawProperty(serializedObject, "privateKey");

                GUILayout.Space(8f);
                GUILayout.Label("Xcode", PlayServWindowTheme.MiniHeadingStyle);
                DrawProperty(serializedObject, "addSignInCapabilityOnBuild");
                DrawProperty(serializedObject, "entitlementsFileName");

                if (serializedObject.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(asset);
                    changed = true;
                }
            }

            return changed;
        }

        private static void DrawProperty(SerializedObject serializedObject, string propertyName)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, includeChildren: true);
        }
    }
}
