using Playserv.AppleSignIn;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor.AppleSignIn
{
    [InitializeOnLoad]
    internal static class PlayServAppleSignInSettingsSectionRegistration
    {
        static PlayServAppleSignInSettingsSectionRegistration()
        {
            PlayServEditorSectionRegistry.Register(
                PlayServEditorSectionIds.AppleSignIn,
                () => new PlayServAppleSignInSettingsSection());
        }
    }

    internal sealed class PlayServAppleSignInSettingsSection : IPlayServEditorSection
    {
        public void Dispose()
        {
        }

        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldAppleSignIn,
                "Identity",
                "Apple Sign In",
                "Configure project-side Apple credential defaults and iOS build capability setup.");

            if (expanded)
            {
                var asset = PlayServAppleSignInSettingsAssetProvider.FindExisting();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton("Create/Select Settings", PlayServWindowButtonTone.Secondary, GUILayout.Height(28f), GUILayout.Width(166f)))
                    {
                        asset = PlayServAppleSignInSettingsAssetProvider.GetOrCreate();
                        Selection.activeObject = asset;
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
                    PlayServWindowChrome.EndSectionCard(expanded);
                    EditorPrefs.SetBool(Const.PrefFoldAppleSignIn, state.FoldAppleSignIn);
                    return;
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
                }
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldAppleSignIn, state.FoldAppleSignIn);
        }

        private static void DrawProperty(SerializedObject serializedObject, string propertyName)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, includeChildren: true);
        }
    }
}
