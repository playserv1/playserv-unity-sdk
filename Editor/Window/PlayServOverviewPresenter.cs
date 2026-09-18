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
                        "Deployment ID",
                        string.IsNullOrWhiteSpace(context.Config != null ? context.Config.DeploymentGameId : null)
                            ? "Not configured"
                            : context.Config.DeploymentGameId,
                        "Editor deployment only");

                    GUILayout.Space(8f);

                    var sdkVersion = PlayServPackageVersionProvider.ResolveInstalledVersion(
                        context.Config != null ? context.Config.SdkVersion : null);
                    DrawSdkVersion(sdkVersion);
                }
            }
        }

        private static void DrawSdkVersion(string sdkVersion)
        {
            var updater = PlayServSdkUpdates.Controller;
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.MetricCardStyle, GUILayout.MinHeight(62f)))
            {
                GUILayout.Label("SDK Version", PlayServWindowTheme.MetricLabelStyle);
                GUILayout.Label(string.IsNullOrWhiteSpace(sdkVersion) ? "Not configured" : sdkVersion, PlayServWindowTheme.MetricValueStyle);
                GUILayout.Label(string.IsNullOrEmpty(updater.Message) ? "Installed package" : updater.Message,
                    PlayServWindowTheme.MetricCaptionStyle);
                GUILayout.Space(6f);
                using (new EditorGUI.DisabledScope(!PlayServSdkUpdates.CanStart))
                {
                    if (PlayServWindowChrome.DrawActionButton("Check for updates", PlayServWindowButtonTone.Secondary))
                        PlayServSdkUpdates.Check();
                    if (!string.IsNullOrEmpty(updater.AvailableVersion) &&
                        PlayServWindowChrome.DrawActionButton("Update to " + updater.AvailableVersion, PlayServWindowButtonTone.Primary))
                        PlayServSdkUpdates.Update();
                }
                if (PlayServWindowChrome.DrawActionButton("Open Package Manager", PlayServWindowButtonTone.Ghost))
                    PlayServSdkUpdates.OpenPackageManager();
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
