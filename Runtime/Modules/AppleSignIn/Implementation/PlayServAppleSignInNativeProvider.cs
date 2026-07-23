using System;

namespace Playserv.AppleSignIn
{
    internal sealed class PlayServAppleSignInNativeProvider : IPlayServAppleSignInProvider
    {
        public event Action<int, PlayServAppleSignInCredential> CredentialReceived
        {
            add => PlayServAppleSignInNative.CredentialReceived += value;
            remove => PlayServAppleSignInNative.CredentialReceived -= value;
        }

        public event Action<int, int, string> SignInFailed
        {
            add => PlayServAppleSignInNative.SignInFailed += value;
            remove => PlayServAppleSignInNative.SignInFailed -= value;
        }

        public event Action<int, PlayServAppleCredentialStateResult> CredentialStateReceived
        {
            add => PlayServAppleSignInNative.CredentialStateReceived += value;
            remove => PlayServAppleSignInNative.CredentialStateReceived -= value;
        }

        public event Action CredentialsRevoked
        {
            add => PlayServAppleSignInNative.CredentialsRevoked += value;
            remove => PlayServAppleSignInNative.CredentialsRevoked -= value;
        }

        public bool IsAvailable => PlayServAppleSignInNative.IsSupported();

        public void SignIn(int requestId, int scopes, string nonce, string state)
        {
            PlayServAppleSignInNative.SignIn(requestId, scopes, nonce, state);
        }

        public void QuickLogin(int requestId, int scopes, string nonce, string state)
        {
            PlayServAppleSignInNative.QuickLogin(requestId, scopes, nonce, state);
        }

        public void GetCredentialState(int requestId, string userId)
        {
            PlayServAppleSignInNative.GetCredentialState(requestId, userId);
        }

        public void SetCredentialsRevokedCallbackEnabled(bool enabled)
        {
            PlayServAppleSignInNative.SetCredentialsRevokedCallbackEnabled(enabled);
        }
    }
}
