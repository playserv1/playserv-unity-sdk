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
            DrawSdkVersionCard(sdkVersion, updater.Message, updater.AvailableVersion, PlayServSdkUpdates.CanStart);
        }

        internal static void DrawSdkVersionCard(string sdkVersion, string message, string availableVersion, bool canStart)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.MetricCardStyle, GUILayout.MinHeight(62f)))
            {
                GUILayout.Label("SDK Version", PlayServWindowTheme.MetricHeadingStyle);
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(22f)))
                {
                    GUILayout.Label(string.IsNullOrWhiteSpace(sdkVersion) ? "Not configured" : sdkVersion,
                        PlayServWindowTheme.MetricVersionStyle, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
                    using (new EditorGUI.DisabledScope(!canStart))
                    {
                        if (DrawVersionAction("Refresh", "↻", "Check for updates"))
                            PlayServSdkUpdates.Check();
                        if (!string.IsNullOrEmpty(availableVersion) &&
                            DrawVersionAction("Download-Available", "↓", "Update to " + availableVersion))
                            PlayServSdkUpdates.Update();
                    }
                    if (DrawVersionAction("Package Manager", "▣", "Open Package Manager"))
                        PlayServSdkUpdates.OpenPackageManager();
                }
                var status = string.IsNullOrEmpty(message) ? "Installed package" : message;
                GUILayout.Label(new GUIContent(status, status), PlayServWindowTheme.MetricStatusStyle,
                    GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            }
        }

        private static bool DrawVersionAction(string iconName, string fallback, string tooltip)
        {
            var icon = EditorGUIUtility.IconContent(iconName);
            return GUILayout.Button(new GUIContent(icon != null ? icon.image : null, tooltip)
                { text = icon != null && icon.image != null ? string.Empty : fallback },
                PlayServWindowTheme.MetricActionStyle, GUILayout.Width(24f), GUILayout.Height(22f));
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
