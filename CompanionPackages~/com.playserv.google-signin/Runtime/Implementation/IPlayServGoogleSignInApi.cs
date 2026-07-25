using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GoogleSignIn
{
    public interface IPlayServGoogleSignInApi
    {
        bool IsAvailable { get; }

        PlayServGoogleSignInSettings Settings { get; }

        Task<PlayServGoogleSignInCredential> SignInAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default);

        Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default);

        void SignOut();

        void Disconnect();
    }
}
