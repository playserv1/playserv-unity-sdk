using System;
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
                var profileLabel = PlayServSdkProfiles.TryGet(settings.ActiveRuntimeProfileId, out var activeProfile)
                    ? activeProfile.Label
                    : "Custom";
                var scopeLabel = settings.HasCurrentPlatformOverride
                    ? $"{settings.CurrentBuildTargetGroupId} override"
                    : "Base project";

                GUILayout.Label($"Active: {profileLabel}", PlayServWindowTheme.SectionSubtitleStyle);
                GUILayout.Space(12f);
                GUILayout.Label($"Scope: {scopeLabel}", PlayServWindowTheme.SectionSubtitleStyle);
                GUILayout.FlexibleSpace();

                if (settings.HasCurrentPlatformOverride)
                {
                    if (PlayServWindowChrome.DrawActionButton(
                            "Clear Override",
                            PlayServWindowButtonTone.Ghost,
                            GUILayout.Width(116f),
                            GUILayout.Height(28f)))
                    {
                        changed |= settings.ClearCurrentPlatformOverride();
                    }
                }
                else if (!string.Equals(
                             settings.CurrentBuildTargetGroupId,
                             BuildTargetGroup.Unknown.ToString(),
                             StringComparison.Ordinal) &&
                         PlayServWindowChrome.DrawActionButton(
                             $"Create {settings.CurrentBuildTargetGroupId} Override",
                             PlayServWindowButtonTone.Ghost,
                             GUILayout.Width(220f),
                             GUILayout.Height(28f)))
                {
                    changed |= settings.CreateCurrentPlatformOverride();
                }
            }

            GUILayout.Space(8f);

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
                "Apply Profile updates module defines and the project-owned generated module selection before Unity reloads scripts.",
                PlayServWindowTheme.SectionSubtitleStyle);

            return changed;
        }
    }
}
