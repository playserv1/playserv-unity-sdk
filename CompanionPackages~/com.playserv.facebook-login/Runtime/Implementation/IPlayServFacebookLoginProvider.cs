using System.Threading;
using System.Threading.Tasks;

namespace Playserv.FacebookLogin
{
    internal interface IPlayServFacebookLoginProvider
    {
        bool IsAvailable { get; }

        bool IsInitialized { get; }

        Task<PlayServFacebookCredential> GetCredentialAsync(
            PlayServFacebookLoginRequest request,
            CancellationToken ct);
    }
}
