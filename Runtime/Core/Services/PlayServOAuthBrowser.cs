using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UnityEngine;

namespace Playserv.Wrapper
{
    internal sealed class PlayServOAuthBrowser : IPlayServOAuthBrowser
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int PlayServOAuth_Open();
        [DllImport("__Internal")] private static extern int PlayServOAuth_Navigate(int id, string url);
        [DllImport("__Internal")] private static extern void PlayServOAuth_Close(int id);
        [DllImport("__Internal")] private static extern int PlayServOAuth_Random(byte[] bytes, int length);
        private readonly int _id;
#endif
        private bool _disposed;
        internal PlayServOAuthBrowser()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _id = PlayServOAuth_Open();
            if (_id == 0) throw new PlayServBrowserAuthException(PlayServAuthErrorCode.BrowserPopupBlocked,
                "The browser blocked the sign-in window. Call LoginBrowserAsync directly from a user click.");
#endif
        }
        internal static byte[] RandomBytes()
        {
            var bytes = new byte[32];
#if UNITY_WEBGL && !UNITY_EDITOR
            if (PlayServOAuth_Random(bytes, bytes.Length) == 0)
                throw new PlayServBrowserAuthException(PlayServAuthErrorCode.InvalidConfiguration, "Browser sign-in requires secure browser randomness.");
#else
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
#endif
            return bytes;
        }
        public void Navigate(string url)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PlayServOAuthBrowser));
#if UNITY_WEBGL && !UNITY_EDITOR
            if (PlayServOAuth_Navigate(_id, url) == 0)
                throw new PlayServBrowserAuthException(PlayServAuthErrorCode.BrowserPopupBlocked, "The sign-in window was closed before navigation.");
#else
            Application.OpenURL(url);
#endif
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            PlayServOAuth_Close(_id);
#endif
        }
    }
}
