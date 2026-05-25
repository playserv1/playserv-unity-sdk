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

        public static bool DrawActionButton(GUIContent content, PlayServWindowButtonTone tone, params GUILayoutOption[] options)
        {
            return GUILayout.Button(content, PlayServWindowTheme.GetButtonStyle(tone), options);
        }

        public static bool DrawIconButton(Texture icon, string fallbackText, string tooltip, PlayServWindowButtonTone tone, float iconSize, params GUILayoutOption[] options)
        {
            var style = PlayServWindowTheme.GetButtonStyle(tone);
            var previousImagePosition = style.imagePosition;
            var previousAlignment = style.alignment;
            var previousFontSize = style.fontSize;

            style.alignment = TextAnchor.MiddleCenter;
            style.imagePosition = icon != null ? ImagePosition.ImageOnly : ImagePosition.TextOnly;
            if (icon == null)
                style.fontSize = Mathf.RoundToInt(iconSize);

            var content = icon != null
                ? new GUIContent(icon, tooltip)
                : new GUIContent(fallbackText, tooltip);
            var clicked = GUILayout.Button(content, style, options);

            style.fontSize = previousFontSize;
            style.alignment = previousAlignment;
            style.imagePosition = previousImagePosition;
            return clicked;
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
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.MetricCardStyle, GUILayout.MinHeight(62f)))
            {
                GUILayout.Label(label, PlayServWindowTheme.MetricLabelStyle);
                GUILayout.Label(value, PlayServWindowTheme.MetricValueStyle);
                GUILayout.Space(3f);
                GUILayout.Label(caption, PlayServWindowTheme.MetricCaptionStyle);
            }
        }
    }
}
