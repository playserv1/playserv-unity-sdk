using System;
using System.Runtime.InteropServices;
using System.Text;

#if UNITY_IOS && !UNITY_EDITOR
using AOT;
#endif

namespace Playserv.AppleSignIn
{
    internal static class PlayServAppleSignInNative
    {
#pragma warning disable 0067
        public static event Action<int, PlayServAppleSignInCredential> CredentialReceived;
        public static event Action<int, int, string> SignInFailed;
        public static event Action<int, PlayServAppleCredentialStateResult> CredentialStateReceived;
        public static event Action CredentialsRevoked;
#pragma warning restore 0067

#if UNITY_IOS && !UNITY_EDITOR
        private delegate void CredentialCallback(
            int requestId,
            int credentialType,
            IntPtr userId,
            IntPtr email,
            IntPtr fullName,
            IntPtr givenName,
            IntPtr familyName,
            IntPtr identityToken,
            IntPtr authorizationCode,
            IntPtr state,
            IntPtr password);

        private delegate void ErrorCallback(int requestId, int code, IntPtr message);

        private delegate void CredentialStateCallback(int requestId, int state, int errorCode, IntPtr errorMessage);

        private delegate void RevokedCallback();

        private static readonly CredentialCallback CredentialCallbackInstance = OnCredentialReceived;
        private static readonly ErrorCallback ErrorCallbackInstance = OnSignInFailed;
        private static readonly CredentialStateCallback CredentialStateCallbackInstance = OnCredentialStateReceived;
        private static readonly RevokedCallback RevokedCallbackInstance = OnCredentialsRevoked;

        [DllImport("__Internal")]
        private static extern bool PlayServAppleSignIn_IsSupported();

        [DllImport("__Internal")]
        private static extern void PlayServAppleSignIn_SignIn(
            int requestId,
            int scopes,
            string nonce,
            string state,
            CredentialCallback success,
            ErrorCallback error);

        [DllImport("__Internal")]
        private static extern void PlayServAppleSignIn_QuickLogin(
            int requestId,
            int scopes,
            string nonce,
            string state,
            CredentialCallback success,
            ErrorCallback error);

        [DllImport("__Internal")]
        private static extern void PlayServAppleSignIn_GetCredentialState(
            int requestId,
            string userId,
            CredentialStateCallback callback);

        [DllImport("__Internal")]
        private static extern void PlayServAppleSignIn_SetCredentialsRevokedCallback(RevokedCallback callback);

        public static bool IsSupported()
        {
            return PlayServAppleSignIn_IsSupported();
        }

        public static void SignIn(int requestId, int scopes, string nonce, string state)
        {
            PlayServAppleSignIn_SignIn(
                requestId,
                scopes,
                nonce ?? string.Empty,
                state ?? string.Empty,
                CredentialCallbackInstance,
                ErrorCallbackInstance);
        }

        public static void QuickLogin(int requestId, int scopes, string nonce, string state)
        {
            PlayServAppleSignIn_QuickLogin(
                requestId,
                scopes,
                nonce ?? string.Empty,
                state ?? string.Empty,
                CredentialCallbackInstance,
                ErrorCallbackInstance);
        }

        public static void GetCredentialState(int requestId, string userId)
        {
            PlayServAppleSignIn_GetCredentialState(requestId, userId ?? string.Empty, CredentialStateCallbackInstance);
        }

        public static void SetCredentialsRevokedCallbackEnabled(bool enabled)
        {
            PlayServAppleSignIn_SetCredentialsRevokedCallback(enabled ? RevokedCallbackInstance : null);
        }

        [MonoPInvokeCallback(typeof(CredentialCallback))]
        private static void OnCredentialReceived(
            int requestId,
            int credentialType,
            IntPtr userId,
            IntPtr email,
            IntPtr fullName,
            IntPtr givenName,
            IntPtr familyName,
            IntPtr identityToken,
            IntPtr authorizationCode,
            IntPtr state,
            IntPtr password)
        {
            CredentialReceived?.Invoke(
                requestId,
                new PlayServAppleSignInCredential(
                    (PlayServAppleCredentialType)credentialType,
                    PtrToStringUtf8(userId),
                    PtrToStringUtf8(email),
                    PtrToStringUtf8(fullName),
                    PtrToStringUtf8(givenName),
                    PtrToStringUtf8(familyName),
                    PtrToStringUtf8(identityToken),
                    PtrToStringUtf8(authorizationCode),
                    PtrToStringUtf8(state),
                    PtrToStringUtf8(password)));
        }

        [MonoPInvokeCallback(typeof(ErrorCallback))]
        private static void OnSignInFailed(int requestId, int code, IntPtr message)
        {
            SignInFailed?.Invoke(requestId, code, PtrToStringUtf8(message));
        }

        [MonoPInvokeCallback(typeof(CredentialStateCallback))]
        private static void OnCredentialStateReceived(int requestId, int state, int errorCode, IntPtr errorMessage)
        {
            CredentialStateReceived?.Invoke(
                requestId,
                new PlayServAppleCredentialStateResult(
                    (PlayServAppleCredentialState)state,
                    errorCode,
                    PtrToStringUtf8(errorMessage)));
        }

        [MonoPInvokeCallback(typeof(RevokedCallback))]
        private static void OnCredentialsRevoked()
        {
            CredentialsRevoked?.Invoke();
        }
#else
        public static bool IsSupported()
        {
            return false;
        }

        public static void SignIn(int requestId, int scopes, string nonce, string state)
        {
            SignInFailed?.Invoke(requestId, -1, "Apple Sign In is available only on iOS 13+ player builds.");
        }

        public static void QuickLogin(int requestId, int scopes, string nonce, string state)
        {
            SignInFailed?.Invoke(requestId, -1, "Apple Sign In is available only on iOS 13+ player builds.");
        }

        public static void GetCredentialState(int requestId, string userId)
        {
            CredentialStateReceived?.Invoke(
                requestId,
                new PlayServAppleCredentialStateResult(
                    PlayServAppleCredentialState.Unknown,
                    -1,
                    "Apple Sign In is available only on iOS 13+ player builds."));
        }

        public static void SetCredentialsRevokedCallbackEnabled(bool enabled)
        {
        }
#endif

        private static string PtrToStringUtf8(IntPtr value)
        {
            if (value == IntPtr.Zero)
                return string.Empty;

            var length = 0;
            while (Marshal.ReadByte(value, length) != 0)
                length++;

            if (length == 0)
                return string.Empty;

            var buffer = new byte[length];
            Marshal.Copy(value, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer, 0, buffer.Length);
        }
    }
}
