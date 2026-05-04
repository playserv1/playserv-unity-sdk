#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServModuleSettingsPresenter
    {
        public void Draw(PlayServWindowContext context)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.HeroCardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("SDK module settings", PlayServWindowTheme.HeroTitleStyle);
                    GUILayout.FlexibleSpace();

                    if (PlayServWindowChrome.DrawActionButton("Back", PlayServWindowButtonTone.Ghost, GUILayout.Width(92f), GUILayout.Height(30f)))
                    {
                        context.State.ShowModuleSettingsLayer = false;
                        context.State.MainScrollPos = Vector2.zero;
                        context.Repaint();
                    }
                }

                GUILayout.Space(6f);
                GUILayout.Label(
                    "Core runtime, transport, serialization, and config stay enabled. Optional editor modules can be hidden from the main control room.",
                    PlayServWindowTheme.HeroBodyStyle);
            }

            GUILayout.Space(12f);

            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardStyle))
            {
                GUILayout.Label("Always enabled", PlayServWindowTheme.MiniHeadingStyle);
                GUILayout.Space(8f);

                DrawLockedModule("Runtime Config", "Game identity, SDK version, endpoints, keepalive, and base runtime settings.");
                GUILayout.Space(6f);
                DrawLockedModule("Transport Core", "Connection lifecycle, transport selection, serialization boundary, and command routing.");
            }

            GUILayout.Space(12f);

            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardStyle))
            {
                GUILayout.Label("Optional modules", PlayServWindowTheme.MiniHeadingStyle);
                GUILayout.Space(8f);

                var settings = context.State.ModuleSettings;
                var changed = false;

                using (new EditorGUI.DisabledScope(context.State.DeployRunning || context.State.VersionSyncRunning))
                {
                    changed |= DrawToggleModule(
                        "Deployment",
                        "Release ZIP preview, version sync, and deploy controls.",
                        settings.Deployment,
                        settings.SetDeployment);
                }

                if (context.State.DeployRunning || context.State.VersionSyncRunning)
                    PlayServWindowChrome.DrawNotice("Deployment module cannot be hidden while deployment/version sync is running.", MessageType.Info);

                GUILayout.Space(6f);
                changed |= DrawToggleModule(
                    "Model Sync",
                    "Schema update checks and generated model refresh controls.",
                    settings.ModelSync,
                    settings.SetModelSync);

                GUILayout.Space(6f);
                changed |= DrawToggleModule(
                    "Events API",
                    "Typed event API generation controls.",
                    settings.Events,
                    settings.SetEvents);

                GUILayout.Space(6f);
                changed |= DrawToggleModule(
                    "DTO Codegen",
                    "Shared DTO generation and cleanup controls.",
                    settings.Codegen,
                    settings.SetCodegen);

                GUILayout.Space(12f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (PlayServWindowChrome.DrawActionButton("Reset Defaults", PlayServWindowButtonTone.Secondary, GUILayout.Width(130f), GUILayout.Height(30f)))
                    {
                        settings.ResetToDefaults();
                        changed = true;
                    }
                }

                if (changed)
                    context.Repaint();
            }
        }

        private static void DrawLockedModule(string title, string description)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardBodyStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(title, PlayServWindowTheme.SectionLabelStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Core", PlayServWindowTheme.SectionPillStyle, GUILayout.Height(22f));
                }

                GUILayout.Space(2f);
                GUILayout.Label(description, PlayServWindowTheme.SectionSubtitleStyle);
            }
        }

        private static bool DrawToggleModule(string title, string description, bool enabled, System.Func<bool, bool> apply)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardBodyStyle))
            {
                var nextEnabled = EditorGUILayout.ToggleLeft(title, enabled);
                GUILayout.Space(2f);
                GUILayout.Label(description, PlayServWindowTheme.SectionSubtitleStyle);

                if (nextEnabled != enabled)
                    return apply(nextEnabled);
            }

            return false;
        }
    }
}
#endif
