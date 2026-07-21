using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.AppleSignIn;

namespace Playserv.Wrapper
{
    public static class PlayServAppleSignIn
    {
        internal static readonly IPlayServAppleSignInApi Api = new PlayServAppleSignInApi();

        public static event Action CredentialsRevoked
        {
            add => Api.CredentialsRevoked += value;
            remove => Api.CredentialsRevoked -= value;
        }

        public static bool IsAvailable => Api.IsAvailable;

        public static PlayServAppleSignInSettings Settings => Api.Settings;

        public static Task<PlayServAppleSignInCredential> SignInAsync(
            PlayServAppleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return Api.SignInAsync(request, ct);
        }

        public static Task<PlayServAppleSignInCredential> QuickLoginAsync(
            PlayServAppleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return Api.QuickLoginAsync(request, ct);
        }

        public static Task<PlayServAppleCredentialStateResult> GetCredentialStateAsync(
            string userId,
            CancellationToken ct = default)
        {
            return Api.GetCredentialStateAsync(userId, ct);
        }
    }
}
