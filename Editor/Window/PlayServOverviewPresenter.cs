using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServOverviewPresenter
    {
        public void DrawHeader(PlayServWindowContext context)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.HeroCardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("PlayServ editor controls", PlayServWindowTheme.HeroTitleStyle);
                    GUILayout.FlexibleSpace();

                    if (PlayServWindowChrome.DrawIconButton(FindSettingsIcon(), "⚙", "Module settings", PlayServWindowButtonTone.Ghost, 18f, GUILayout.Width(34f), GUILayout.Height(30f)))
                    {
                        context.State.ShowModuleSettingsLayer = true;
                        context.State.MainScrollPos = Vector2.zero;
                        context.Repaint();
                    }
                }

                GUILayout.Space(6f);
                GUILayout.Label("Configure runtime, sync models, deploy code, and generate APIs from one place.", PlayServWindowTheme.HeroAccentStyle);
            }

            GUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    PlayServWindowChrome.DrawOverviewCard(
                        "Game ID",
                        string.IsNullOrWhiteSpace(context.Config != null ? context.Config.GameId : null) ? "Not configured" : context.Config.GameId,
                        "Runtime identity");
                    GUILayout.Space(8f);
                    PlayServWindowChrome.DrawOverviewCard(
                        "Backend",
                        string.IsNullOrWhiteSpace(context.Config != null ? context.Config.BackendServerAddress : null) ? "Not set" : context.Config.BackendServerAddress,
                        "Primary endpoint");
                }

                if (PlayServEditorModuleAvailability.EditorModelSync ||
                    PlayServEditorModuleAvailability.EditorDeployment)
                {
                    GUILayout.Space(8f);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (PlayServEditorModuleAvailability.EditorModelSync)
                            PlayServWindowChrome.DrawOverviewCard("Schema", EditorPrefs.GetString(Const.PrefKeyJsonSchemaVersion, "—"), "Current model hash");

                        if (PlayServEditorModuleAvailability.EditorModelSync &&
                            PlayServEditorModuleAvailability.EditorDeployment)
                        {
                            GUILayout.Space(8f);
                        }

                        if (PlayServEditorModuleAvailability.EditorDeployment)
                        {
                            PlayServWindowChrome.DrawOverviewCard(
                                "Deploy",
                                context.State.DeployRunning ? "Deploying…" : context.State.VersionSyncRunning ? "Syncing…" : "Ready",
                                "Release control");
                        }
                    }
                }
            }
        }

        private static Texture FindSettingsIcon()
        {
            var icon = EditorGUIUtility.IconContent("SettingsIcon") ?? EditorGUIUtility.IconContent("_Popup");
            return icon != null ? icon.image : null;
        }

        public void DrawFooter(PlayServWindowContext context)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.FooterCardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool showOnStartup = EditorPrefs.GetBool(Const.PrefKeyShowOnStartup, true);
                    bool newShowOnStartup = EditorGUILayout.ToggleLeft("Show this window on Unity startup", showOnStartup);

                    if (newShowOnStartup != showOnStartup)
                        EditorPrefs.SetBool(Const.PrefKeyShowOnStartup, newShowOnStartup);

                    GUILayout.FlexibleSpace();

                    if (PlayServWindowChrome.DrawActionButton("Ping Config", PlayServWindowButtonTone.Secondary, GUILayout.Width(116f), GUILayout.Height(30f)))
                        context.FocusConfigAsset();

                    GUILayout.Space(8f);

                    if (PlayServWindowChrome.DrawActionButton("Open Docs", PlayServWindowButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(30f)))
                        Application.OpenURL(context.DocsUrl);
                }
            }
        }
    }
}
