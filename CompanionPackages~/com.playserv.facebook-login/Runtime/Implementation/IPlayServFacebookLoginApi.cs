using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.FacebookLogin
{
    public interface IPlayServFacebookLoginApi
    {
        bool IsAvailable { get; }

        bool IsInitialized { get; }

        Task<PlayServFacebookCredential> GetCredentialAsync(
            PlayServFacebookLoginRequest request = null,
            CancellationToken ct = default);

        Task<PlayServAuthResult> LoginAsync(
            PlayServFacebookLoginRequest request = null,
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default);

        Task<PlayServAuthResult> LinkAsync(
            PlayServFacebookLoginRequest request = null,
            CancellationToken ct = default);
    }
}
