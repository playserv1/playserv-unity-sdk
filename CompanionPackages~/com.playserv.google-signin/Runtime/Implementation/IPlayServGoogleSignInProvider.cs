using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GoogleSignIn
{
    internal interface IPlayServGoogleSignInProvider
    {
        bool IsAvailable { get; }

        Task<PlayServGoogleSignInCredential> SignInAsync(
            PlayServGoogleSignInSettings settings,
            PlayServGoogleSignInRequest request,
            CancellationToken ct);

        Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
            PlayServGoogleSignInSettings settings,
            PlayServGoogleSignInRequest request,
            CancellationToken ct);

        void SignOut();

        void Disconnect();
    }
}
