using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.AppleSignIn
{
    public interface IPlayServAppleSignInApi
    {
        event Action CredentialsRevoked;

        bool IsAvailable { get; }

        PlayServAppleSignInSettings Settings { get; }

        Task<PlayServAppleSignInCredential> SignInAsync(
            PlayServAppleSignInRequest request = null,
            CancellationToken ct = default);

        Task<PlayServAppleSignInCredential> QuickLoginAsync(
            PlayServAppleSignInRequest request = null,
            CancellationToken ct = default);

        Task<PlayServAppleCredentialStateResult> GetCredentialStateAsync(
            string userId,
            CancellationToken ct = default);
    }
}
