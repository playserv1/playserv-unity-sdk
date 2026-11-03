using System;

namespace Playserv.AppleSignIn
{
    internal interface IPlayServAppleSignInProvider
    {
        event Action<int, PlayServAppleSignInCredential> CredentialReceived;

        event Action<int, int, string> SignInFailed;

        event Action<int, PlayServAppleCredentialStateResult> CredentialStateReceived;

        event Action CredentialsRevoked;

        bool IsAvailable { get; }

        void SignIn(int requestId, int scopes, string nonce, string state);

        void QuickLogin(int requestId, int scopes, string nonce, string state);

        void GetCredentialState(int requestId, string userId);

        void SetCredentialsRevokedCallbackEnabled(bool enabled);
    }
}
