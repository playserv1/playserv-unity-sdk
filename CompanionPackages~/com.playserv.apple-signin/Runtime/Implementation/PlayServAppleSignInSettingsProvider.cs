#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Playserv.AppleSignIn
{
    public static class PlayServAppleSignInSettingsProvider
    {
        public const string ResourceName = "PlayServAppleSignInSettings";
        private static PlayServAppleSignInSettings _fallback;

        public static PlayServAppleSignInSettings LoadOrDefault()
        {
            var settings = Resources.Load<PlayServAppleSignInSettings>(ResourceName);
            if (settings != null)
                return settings;

            if (_fallback == null)
                _fallback = ScriptableObject.CreateInstance<PlayServAppleSignInSettings>();

            return _fallback;
        }
    }
}
#endif
