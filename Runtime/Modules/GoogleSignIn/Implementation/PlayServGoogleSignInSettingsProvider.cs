#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Playserv.GoogleSignIn
{
    public static class PlayServGoogleSignInSettingsProvider
    {
        public const string ResourceName = "PlayServGoogleSignInSettings";
        private static PlayServGoogleSignInSettings _fallback;

        public static PlayServGoogleSignInSettings LoadOrDefault()
        {
            var settings = Resources.Load<PlayServGoogleSignInSettings>(ResourceName);
            if (settings != null)
                return settings;

            if (_fallback == null)
                _fallback = ScriptableObject.CreateInstance<PlayServGoogleSignInSettings>();

            return _fallback;
        }
    }
}
#endif
