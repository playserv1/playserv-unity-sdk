using Playserv.GoogleSignIn;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor.GoogleSignIn
{
    [InitializeOnLoad]
    internal static class PlayServGoogleSignInSettingsSectionRegistration
    {
        static PlayServGoogleSignInSettingsSectionRegistration()
        {
            PlayServEditorSectionRegistry.Register(
                PlayServEditorSectionIds.GoogleSignIn,
                () => new PlayServGoogleSignInSettingsSection());

            PlayServModuleConfigSectionRegistry.Register(
                PlayServModuleManifest.GoogleSignInId,
                PlayServEditorSectionIds.GoogleSignIn,
                order: 90);
        }
    }

    internal sealed class PlayServGoogleSignInSettingsSection : IPlayServEditorSection
    {
        public void Dispose()
        {
        }

        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldGoogleSignIn,
                "Identity",
                "Google Sign In",
                "Configure Google OAuth defaults for ID tokens and server auth codes.");

            if (expanded)
            {
                var asset = PlayServGoogleSignInSettingsAssetProvider.FindExisting();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton("Create/Select Settings", PlayServWindowButtonTone.Secondary, GUILayout.Height(28f), GUILayout.Width(166f)))
                    {
                        asset = PlayServGoogleSignInSettingsAssetProvider.GetOrCreate();
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
                    PlayServWindowChrome.DrawNotice("Create a settings asset to edit Google credentials.", MessageType.Info);
                    PlayServWindowChrome.EndSectionCard(expanded);
                    EditorPrefs.SetBool(Const.PrefFoldGoogleSignIn, state.FoldGoogleSignIn);
                    return;
                }

                var serializedObject = new SerializedObject(asset);
                serializedObject.Update();

                GUILayout.Label("OAuth", PlayServWindowTheme.MiniHeadingStyle);
                DrawProperty(serializedObject, "webClientId");
                DrawProperty(serializedObject, "requestIdToken");
                DrawProperty(serializedObject, "requestAuthCode");
                DrawProperty(serializedObject, "requestEmail");
                DrawProperty(serializedObject, "forceTokenRefresh");
                DrawProperty(serializedObject, "useGameSignIn");

                GUILayout.Space(8f);
                GUILayout.Label("Account", PlayServWindowTheme.MiniHeadingStyle);
                DrawProperty(serializedObject, "hostedDomain");
                DrawProperty(serializedObject, "accountName");
                DrawProperty(serializedObject, "additionalScopes");

                if (serializedObject.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(asset);
                }
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldGoogleSignIn, state.FoldGoogleSignIn);
        }

        private static void DrawProperty(SerializedObject serializedObject, string propertyName)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, includeChildren: true);
        }
    }
}
