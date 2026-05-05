#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal enum PlayServWindowButtonTone
    {
        Primary,
        Secondary,
        Ghost,
        Danger
    }

    internal static class PlayServWindowTheme
    {
        public static readonly Color Background = Parse("#0E1117");
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.08f);

        public static GUIStyle HeroCardStyle { get; private set; }
        public static GUIStyle HeroTitleStyle { get; private set; }
        public static GUIStyle HeroAccentStyle { get; private set; }
        public static GUIStyle HeroBodyStyle { get; private set; }
        public static GUIStyle PillStyle { get; private set; }
        public static GUIStyle MetricCardStyle { get; private set; }
        public static GUIStyle MetricLabelStyle { get; private set; }
        public static GUIStyle MetricValueStyle { get; private set; }
        public static GUIStyle MetricCaptionStyle { get; private set; }
        public static GUIStyle CardStyle { get; private set; }
        public static GUIStyle CardBodyStyle { get; private set; }
        public static GUIStyle SectionPillStyle { get; private set; }
        public static GUIStyle ModuleTagEnabledStyle { get; private set; }
        public static GUIStyle ModuleTagDisabledStyle { get; private set; }
        public static GUIStyle ModuleTagBlockedStyle { get; private set; }
        public static GUIStyle SectionTitleButtonStyle { get; private set; }
        public static GUIStyle SectionSubtitleStyle { get; private set; }
        public static GUIStyle MiniHeadingStyle { get; private set; }
        public static GUIStyle SectionLabelStyle { get; private set; }
        public static GUIStyle StatusValueStyle { get; private set; }
        public static GUIStyle InputStyle { get; private set; }
        public static GUIStyle TextAreaStyle { get; private set; }
        public static GUIStyle NoticeInfoStyle { get; private set; }
        public static GUIStyle NoticeWarningStyle { get; private set; }
        public static GUIStyle LogContainerStyle { get; private set; }
        public static GUIStyle LogLineStyle { get; private set; }
        public static GUIStyle EmptyStateStyle { get; private set; }
        public static GUIStyle FooterCardStyle { get; private set; }

        private static GUIStyle _primaryButtonStyle;
        private static GUIStyle _secondaryButtonStyle;
        private static GUIStyle _ghostButtonStyle;
        private static GUIStyle _dangerButtonStyle;
        private static Texture2D _transparentTexture;

        public static void Ensure()
        {
            if (HeroCardStyle != null)
                return;

            HeroCardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(18, 18, 18, 18), new RectOffset(0, 0, 0, 0));
            MetricCardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(10, 10, 8, 8), new RectOffset(0, 0, 0, 0));
            CardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(18, 18, 14, 14), new RectOffset(0, 0, 0, 0));
            CardBodyStyle = CreateBoxStyle("#0F1319", "#1E232B", new RectOffset(10, 10, 10, 10), new RectOffset(0, 0, 0, 0));
            FooterCardStyle = CreateBoxStyle("#12161D", "#262C36", new RectOffset(18, 18, 16, 16), new RectOffset(0, 0, 0, 0));
            LogContainerStyle = CreateBoxStyle("#0F1319", "#1E232B", new RectOffset(12, 12, 10, 10), new RectOffset(0, 0, 0, 0));
            NoticeInfoStyle = CreateBoxStyle("#141B26", "#293446", new RectOffset(12, 12, 10, 10), new RectOffset(0, 0, 0, 0));
            NoticeWarningStyle = CreateBoxStyle("#241C17", "#4A392A", new RectOffset(12, 12, 10, 10), new RectOffset(0, 0, 0, 0));

            PillStyle = CreateChipStyle("#171B22", "#2A303A", "#C3CBD8", 11, FontStyle.Bold, new RectOffset(10, 10, 5, 5));
            SectionPillStyle = CreateChipStyle("#171B22", "#2A303A", "#8CB2FF", 10, FontStyle.Bold, new RectOffset(8, 8, 4, 4));
            ModuleTagEnabledStyle = CreateChipStyle("#10291D", "#1E6B42", "#80E6A7", 10, FontStyle.Bold, new RectOffset(8, 8, 4, 4));
            ModuleTagDisabledStyle = CreateChipStyle("#1A1E25", "#303743", "#7D8794", 10, FontStyle.Bold, new RectOffset(8, 8, 4, 4));
            ModuleTagBlockedStyle = CreateChipStyle("#2C2215", "#6E4D1F", "#F2B66D", 10, FontStyle.Bold, new RectOffset(8, 8, 4, 4));

            HeroTitleStyle = CreateWrappedLabelStyle(30, FontStyle.Bold, "#F3F5F8");
            HeroAccentStyle = CreateWrappedLabelStyle(18, FontStyle.Bold, "#A8D3FF");
            HeroBodyStyle = CreateWrappedLabelStyle(13, FontStyle.Normal, "#AAB2BF");

            MetricLabelStyle = CreateLabelStyle(10, FontStyle.Bold, "#8892A0");
            MetricValueStyle = CreateWrappedLabelStyle(13, FontStyle.Bold, "#F3F5F8");
            MetricCaptionStyle = CreateWrappedLabelStyle(10, FontStyle.Normal, "#7D8693");

            SectionTitleButtonStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                stretchWidth = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Parse("#F6F7FF"), background = TransparentTexture },
                hover = { textColor = Parse("#8BC8FF"), background = TransparentTexture }
            };

            SectionSubtitleStyle = CreateWrappedLabelStyle(12, FontStyle.Normal, "#8D97A5");
            MiniHeadingStyle = CreateLabelStyle(11, FontStyle.Bold, "#AAB3BF");
            SectionLabelStyle = CreateLabelStyle(11, FontStyle.Bold, "#C3CBD8");
            StatusValueStyle = CreateLabelStyle(12, FontStyle.Bold, "#F3F5F8");
            LogLineStyle = CreateWrappedLabelStyle(11, FontStyle.Normal, "#B3BBC7");
            EmptyStateStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = Parse("#7F88A6") }
            };

            InputStyle = CreateInputStyle("#0F1319", "#262C36", "#E9EEFF");
            TextAreaStyle = CreateTextAreaStyle("#0F1319", "#262C36", "#E9EEFF");

            _primaryButtonStyle = CreateButtonStyle("#6E56CF", "#7B63DB", "#F8F7FF", 12, FontStyle.Bold);
            _secondaryButtonStyle = CreateButtonStyle("#1A1F27", "#202632", "#F0F3FF", 12, FontStyle.Normal);
            _ghostButtonStyle = CreateButtonStyle("#12161D", "#191E26", "#AAB4C0", 12, FontStyle.Normal);
            _dangerButtonStyle = CreateButtonStyle("#362126", "#41282E", "#FFD7E6", 12, FontStyle.Bold);
        }

        public static GUIStyle GetButtonStyle(PlayServWindowButtonTone tone)
        {
            Ensure();
            switch (tone)
            {
                case PlayServWindowButtonTone.Primary:
                    return _primaryButtonStyle;
                case PlayServWindowButtonTone.Ghost:
                    return _ghostButtonStyle;
                case PlayServWindowButtonTone.Danger:
                    return _dangerButtonStyle;
                default:
                    return _secondaryButtonStyle;
            }
        }

        private static Texture2D TransparentTexture
        {
            get
            {
                if (_transparentTexture != null)
                    return _transparentTexture;

                _transparentTexture = new Texture2D(1, 1);
                _transparentTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                _transparentTexture.Apply();
                _transparentTexture.hideFlags = HideFlags.HideAndDontSave;
                return _transparentTexture;
            }
        }

        private static GUIStyle CreateBoxStyle(string fillHex, string borderHex, RectOffset padding, RectOffset margin)
        {
            var texture = CreateBorderedTexture(Parse(fillHex), Parse(borderHex));
            return new GUIStyle(GUI.skin.box)
            {
                normal = { background = texture, textColor = Parse("#F5F7FF") },
                border = new RectOffset(3, 3, 3, 3),
                padding = padding,
                margin = margin
            };
        }

        private static GUIStyle CreateChipStyle(string fillHex, string borderHex, string textHex, int fontSize, FontStyle fontStyle, RectOffset padding)
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = TextAnchor.MiddleCenter,
                padding = padding,
                margin = new RectOffset(0, 0, 0, 0),
                normal =
                {
                    background = CreateBorderedTexture(Parse(fillHex), Parse(borderHex)),
                    textColor = Parse(textHex)
                },
                border = new RectOffset(3, 3, 3, 3)
            };
        }

        private static GUIStyle CreateButtonStyle(string fillHex, string hoverHex, string textHex, int fontSize, FontStyle fontStyle)
        {
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(3, 3, 3, 3),
                padding = new RectOffset(14, 14, 8, 8),
                margin = new RectOffset(0, 0, 0, 0),
                normal =
                {
                    background = CreateBorderedTexture(Parse(fillHex), Shift(Parse(fillHex), 0.18f)),
                    textColor = Parse(textHex)
                },
                hover =
                {
                    background = CreateBorderedTexture(Parse(hoverHex), Shift(Parse(hoverHex), 0.12f)),
                    textColor = Parse("#FFFFFF")
                },
                active =
                {
                    background = CreateBorderedTexture(Shift(Parse(fillHex), -0.08f), Shift(Parse(fillHex), 0.08f)),
                    textColor = Parse(textHex)
                }
            };
        }

        private static GUIStyle CreateInputStyle(string fillHex, string borderHex, string textHex)
        {
            return new GUIStyle(EditorStyles.textField)
            {
                fontSize = 12,
                border = new RectOffset(3, 3, 3, 3),
                padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(0, 0, 0, 0),
                normal =
                {
                    background = CreateBorderedTexture(Parse(fillHex), Parse(borderHex)),
                    textColor = Parse(textHex)
                },
                focused =
                {
                    background = CreateBorderedTexture(Parse(fillHex), Shift(Parse(borderHex), 0.12f)),
                    textColor = Parse(textHex)
                }
            };
        }

        private static GUIStyle CreateTextAreaStyle(string fillHex, string borderHex, string textHex)
        {
            var style = CreateInputStyle(fillHex, borderHex, textHex);
            style.wordWrap = true;
            style.alignment = TextAnchor.UpperLeft;
            style.stretchHeight = true;
            return style;
        }

        private static GUIStyle CreateLabelStyle(int fontSize, FontStyle fontStyle, string textHex)
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                normal = { textColor = Parse(textHex) }
            };
        }

        private static GUIStyle CreateWrappedLabelStyle(int fontSize, FontStyle fontStyle, string textHex)
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                wordWrap = true,
                normal = { textColor = Parse(textHex) }
            };
        }

        private static Texture2D CreateBorderedTexture(Color fill, Color border)
        {
            var tex = new Texture2D(8, 8);
            var pixels = new Color[64];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var index = y * 8 + x;
                    var isBorder = x <= 1 || y <= 1 || x >= 6 || y >= 6;
                    pixels[index] = isBorder ? border : fill;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Color Parse(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }

        private static Color Shift(Color color, float delta)
        {
            return new Color(
                Mathf.Clamp01(color.r + delta),
                Mathf.Clamp01(color.g + delta),
                Mathf.Clamp01(color.b + delta),
                color.a);
        }
    }
}
#endif
