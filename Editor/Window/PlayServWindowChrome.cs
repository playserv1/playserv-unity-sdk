#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServWindowChrome
    {
        public static bool BeginSectionCard(ref bool expanded, string badge, string title, string subtitle)
        {
            EditorGUILayout.BeginVertical(PlayServWindowTheme.CardStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(badge, PlayServWindowTheme.SectionPillStyle, GUILayout.Height(22f));
                GUILayout.FlexibleSpace();

                if (DrawActionButton(expanded ? "Collapse" : "Expand", PlayServWindowButtonTone.Ghost, GUILayout.Width(92f), GUILayout.Height(28f)))
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

        public static void EndSectionCard(bool expanded)
        {
            if (expanded)
                EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        public static bool DrawActionButton(string label, PlayServWindowButtonTone tone, params GUILayoutOption[] options)
        {
            return GUILayout.Button(label, PlayServWindowTheme.GetButtonStyle(tone), options);
        }

        public static void DrawNotice(string text, MessageType type)
        {
            var style = type == MessageType.Warning
                ? PlayServWindowTheme.NoticeWarningStyle
                : PlayServWindowTheme.NoticeInfoStyle;

            GUILayout.Label(text, style);
        }

        public static void DrawReadOnlyTextField(string label, string value, float fieldHeight)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(
                    value ?? string.Empty,
                    PlayServWindowTheme.InputStyle,
                    GUILayout.Height(fieldHeight));
            }
        }

        public static void DrawOverviewCard(string label, string value, string caption)
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
    }
}
#endif
