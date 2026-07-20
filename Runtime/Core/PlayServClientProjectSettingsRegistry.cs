using System;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Editor-only project test knobs shared between the PlayServ window UI and game code.
    /// Values live in EditorPrefs (not in the config asset or a scene), so flipping them never
    /// dirties versioned files and they are inert in player builds.
    /// </summary>
    public static class PlayServEditorTestFlags
    {
        /// <summary>EditorPrefs key: when true, the game enters battles without server AI bots.</summary>
        public const string DisableAiBotsPrefKey = "PlayServ.Project.DisableAiBots";
    }

    public static class PlayServClientProjectSettingsRegistry
    {
        private static IPlayServClientProjectSettingsProvider _provider;
        private static IPlayServClientProjectSettingsProvider _fallbackProvider;

        public static bool HasProvider => _provider != null;

        public static void Register(IPlayServClientProjectSettingsProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public static void RegisterFallback(IPlayServClientProjectSettingsProvider provider)
        {
            _fallbackProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public static void Clear(IPlayServClientProjectSettingsProvider provider)
        {
            if (ReferenceEquals(_provider, provider))
                _provider = null;
        }

        public static void ClearFallback(IPlayServClientProjectSettingsProvider provider)
        {
            if (ReferenceEquals(_fallbackProvider, provider))
                _fallbackProvider = null;
        }

        public static bool TryResolveSettings(PlayServConfig config, out PlayServSettings settings)
        {
            settings = null;

            var provider = ResolveProvider();
            if (provider == null)
                return false;

            try
            {
                return provider.TryResolveSettings(config, out settings);
            }
            catch
            {
                settings = null;
                return false;
            }
        }

#if UNITY_EDITOR
        public static bool TryDrawProjectConfigUi(out bool changed)
        {
            changed = false;

            var provider = ResolveProvider();
            if (provider == null)
                return false;

            try
            {
                return provider.TryDrawProjectConfigUi(out changed);
            }
            catch
            {
                changed = false;
                return false;
            }
        }
#endif

        private static IPlayServClientProjectSettingsProvider ResolveProvider()
        {
            return _provider ?? _fallbackProvider;
        }
    }
}
