using System;

namespace Playserv.Wrapper
{
    public static class PlayServClientProjectSettingsRegistry
    {
        private static IPlayServClientProjectSettingsProvider _provider;

        public static bool HasProvider => _provider != null;

        public static void Register(IPlayServClientProjectSettingsProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public static void Clear(IPlayServClientProjectSettingsProvider provider)
        {
            if (ReferenceEquals(_provider, provider))
                _provider = null;
        }

        public static bool TryResolveSettings(PlayServConfig config, out PlayServSettings settings)
        {
            settings = null;

            var provider = _provider;
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

            var provider = _provider;
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
    }
}
