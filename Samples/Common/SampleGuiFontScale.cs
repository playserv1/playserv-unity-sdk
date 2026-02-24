using System.Collections.Generic;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// Applies consistent IMGUI font scaling for samples overlays.
    /// </summary>
    internal static class SampleGuiFontScale
    {
        private const float FontScale = 1.3f;
        private const int FallbackFontSize = 14;

        private static readonly Dictionary<GUIStyle, int> BaseFontSizes = new Dictionary<GUIStyle, int>();

        public static void Apply()
        {
            var skin = GUI.skin;
            if (skin == null)
                return;

            ScaleStyle(skin.label);
            ScaleStyle(skin.button);
            ScaleStyle(skin.box);
            ScaleStyle(skin.toggle);
            ScaleStyle(skin.textField);
            ScaleStyle(skin.textArea);

            var customStyles = skin.customStyles;
            if (customStyles == null)
                return;

            for (var i = 0; i < customStyles.Length; i++)
                ScaleStyle(customStyles[i]);
        }

        private static void ScaleStyle(GUIStyle style)
        {
            if (style == null)
                return;

            if (!BaseFontSizes.TryGetValue(style, out var baseSize))
            {
                baseSize = style.fontSize > 0 ? style.fontSize : FallbackFontSize;
                BaseFontSizes[style] = baseSize;
            }

            var scaledSize = Mathf.Max(1, Mathf.RoundToInt(baseSize * FontScale));
            if (style.fontSize != scaledSize)
                style.fontSize = scaledSize;
        }
    }
}
