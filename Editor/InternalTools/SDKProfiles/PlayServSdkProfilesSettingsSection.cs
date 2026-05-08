using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServSdkProfilesSettingsSectionRegistration
    {
        static PlayServSdkProfilesSettingsSectionRegistration()
        {
            PlayServModuleSettingsSectionRegistry.Register(new PlayServSdkProfilesSettingsSection());
        }
    }

    internal sealed class PlayServSdkProfilesSettingsSection : IPlayServModuleSettingsSection
    {
        public int Order => 100;

        public bool Draw(
            PlayServWindowContext context,
            PlayServEditorModuleSettings settings,
            bool hasRuntimeModules,
            bool hasServerRuntimeModules)
        {
            if (!hasRuntimeModules && !hasServerRuntimeModules)
                return false;

            var changed = false;

            GUILayout.Space(12f);
            GUILayout.Label("SDK profiles", PlayServWindowTheme.MiniHeadingStyle);
            GUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var profile in PlayServSdkProfiles.All)
                {
                    if (PlayServWindowChrome.DrawActionButton($"Apply {profile.Label}", PlayServWindowButtonTone.Secondary, GUILayout.Height(28f)))
                        changed |= PlayServRuntimeModuleLifecycle.ApplyProfile(settings, profile);

                    GUILayout.Space(6f);
                }

                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(2f);
            GUILayout.Label(
                "Apply Profile updates module defines, generated compatibility code, and root asmdef references before Unity reloads scripts.",
                PlayServWindowTheme.SectionSubtitleStyle);

            return changed;
        }
    }
}
