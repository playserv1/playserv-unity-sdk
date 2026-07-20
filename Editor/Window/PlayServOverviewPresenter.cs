using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

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

                    var dashboardAddress = ResolveDashboardAddress(context);
                    if (!string.IsNullOrWhiteSpace(dashboardAddress))
                    {
                        if (PlayServWindowChrome.DrawActionButton("Dashboard", PlayServWindowButtonTone.Ghost, GUILayout.Width(104f), GUILayout.Height(30f)))
                            Application.OpenURL(dashboardAddress);

                        GUILayout.Space(8f);
                    }

                    if (PlayServWindowChrome.DrawIconButton(FindSettingsIcon(), "⚙", "Module settings", PlayServWindowButtonTone.Ghost, 24f, GUILayout.Width(42f), GUILayout.Height(30f)))
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

                    var sdkVersion = PlayServPackageVersionProvider.ResolveInstalledVersion(
                        context.Config != null ? context.Config.SdkVersion : null);
                    PlayServWindowChrome.DrawOverviewCard(
                        "SDK Version",
                        string.IsNullOrWhiteSpace(sdkVersion) ? "Not configured" : sdkVersion,
                        "Installed package");
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

                    if (PlayServWindowChrome.DrawActionButton("Open Docs", PlayServWindowButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(30f)))
                        Application.OpenURL(context.DocsUrl);
                }
            }
        }

        private static string ResolveDashboardAddress(PlayServWindowContext context)
        {
            var configured = context.Config != null ? context.Config.DashboardAddress : null;
            return PlayServPackageDefaultsProvider.ResolveDashboardAddress(configured);
        }
    }
}
