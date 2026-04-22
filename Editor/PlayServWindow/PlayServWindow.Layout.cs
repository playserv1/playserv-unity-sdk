#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    public sealed partial class PlayServWindow
    {
        private void OnGUI()
        {
            PlayServWindowTheme.Ensure();
            DrawWindowBackdrop();

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.23f, 120f, 170f);

            using (new GUILayout.AreaScope(new Rect(0f, 0f, position.width, position.height)))
            {
                _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(24f);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Space(18f);
                        DrawHeroSection();
                        GUILayout.Space(14f);
                        DrawOverviewStrip();
                        GUILayout.Space(18f);

                        DrawConfigFoldout();
                        GUILayout.Space(12f);
                        DrawDeploymentFoldout();
                        GUILayout.Space(12f);
                        DrawModelFoldout();
                        GUILayout.Space(12f);
                        DrawEventsFoldout();
                        GUILayout.Space(12f);
                        DrawCodegenFoldout();

                        if (ShowWebSocketConnectionMenu)
                        {
                            GUILayout.Space(12f);
                            DrawConnectionFoldout();
                        }

                        GUILayout.Space(16f);
                        DrawFooter();
                        GUILayout.Space(18f);
                    }
                    GUILayout.Space(24f);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;

            if (_deployRunning)
                Repaint();
        }

        private void DrawWindowBackdrop()
        {
            var fullRect = new Rect(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(fullRect, PlayServWindowTheme.Background);
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, 1f), PlayServWindowTheme.GridLine);
        }

        private void DrawHeroSection()
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.HeroCardStyle))
            {
                GUILayout.Label("PlayServ editor controls", PlayServWindowTheme.HeroTitleStyle);
                GUILayout.Space(6f);
                GUILayout.Label("Configure runtime, sync models, deploy code, and generate APIs from one place.", PlayServWindowTheme.HeroAccentStyle);
            }
        }

        private void DrawOverviewStrip()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawOverviewCard("Game ID", string.IsNullOrWhiteSpace(_config?.GameId) ? "Not configured" : _config.GameId, "Runtime identity");
                    GUILayout.Space(10f);
                    DrawOverviewCard(
                        "Backend",
                        string.IsNullOrWhiteSpace(_config?.BackendServerAddress) ? "Not set" : _config.BackendServerAddress,
                        "Primary transport");
                }

                GUILayout.Space(10f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawOverviewCard("Schema", EditorPrefs.GetString(Const.PrefKeyJsonSchemaVersion, "—"), "Current model hash");
                    GUILayout.Space(10f);
                    DrawOverviewCard("Deploy", _deployRunning ? "Deploying…" : _versionSyncRunning ? "Syncing…" : "Ready", "Release control");
                }
            }
        }

        private void DrawOverviewCard(string label, string value, string caption)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.MetricCardStyle, GUILayout.MinHeight(82f)))
            {
                GUILayout.Label(label, PlayServWindowTheme.MetricLabelStyle);
                GUILayout.Space(2f);
                GUILayout.Label(value, PlayServWindowTheme.MetricValueStyle);
                GUILayout.Space(6f);
                GUILayout.Label(caption, PlayServWindowTheme.MetricCaptionStyle);
            }
        }

        private bool BeginSectionCard(ref bool expanded, string badge, string title, string subtitle)
        {
            EditorGUILayout.BeginVertical(PlayServWindowTheme.CardStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(badge, PlayServWindowTheme.SectionPillStyle, GUILayout.Height(22f));
                GUILayout.FlexibleSpace();

                if (DrawActionButton(expanded ? "Collapse" : "Expand", ButtonTone.Ghost, GUILayout.Width(92f), GUILayout.Height(24f)))
                    expanded = !expanded;
            }

            GUILayout.Space(6f);

            if (GUILayout.Button(title, PlayServWindowTheme.SectionTitleButtonStyle, GUILayout.Height(28f)))
                expanded = !expanded;

            GUILayout.Space(2f);
            GUILayout.Label(subtitle, PlayServWindowTheme.SectionSubtitleStyle);

            if (expanded)
            {
                GUILayout.Space(12f);
                EditorGUILayout.BeginVertical(PlayServWindowTheme.CardBodyStyle);
                return true;
            }

            return false;
        }

        private static void EndSectionCard(bool expanded)
        {
            if (expanded)
                EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        private bool DrawActionButton(string label, ButtonTone tone, params GUILayoutOption[] options)
        {
            return GUILayout.Button(label, PlayServWindowTheme.GetButtonStyle(tone), options);
        }

        private static void DrawNotice(string text, MessageType type)
        {
            var style = type == MessageType.Warning
                ? PlayServWindowTheme.NoticeWarningStyle
                : PlayServWindowTheme.NoticeInfoStyle;

            GUILayout.Label(text, style);
        }

        private void FocusConfigAsset()
        {
            EnsureConfig();
            if (_config == null)
                return;

            Selection.activeObject = _config;
            EditorGUIUtility.PingObject(_config);
        }

        private static void DrawReadOnlyTextField(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(
                    value ?? string.Empty,
                    PlayServWindowTheme.InputStyle,
                    GUILayout.Height(StyledFieldHeight));
            }
        }

        private void DrawFooter()
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

                    if (DrawActionButton("Ping Config", ButtonTone.Secondary, GUILayout.Width(116f), GUILayout.Height(28f)))
                        FocusConfigAsset();

                    GUILayout.Space(8f);

                    if (DrawActionButton("Open Docs", ButtonTone.Secondary, GUILayout.Width(112f), GUILayout.Height(28f)))
                        Application.OpenURL(DocsUrl);
                }
            }
        }
    }
}
#endif
