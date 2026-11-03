using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.EpicAuth
{
    public interface IPlayServEpicAuthApi
    {
        bool IsAvailable { get; }

        bool IsInitialized { get; }

        Task<PlayServEpicCredential> GetCredentialAsync(
            PlayServEpicAuthRequest request,
            CancellationToken ct = default);

        Task<PlayServAuthResult> LoginAsync(
            PlayServEpicAuthRequest request,
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default);

        Task<PlayServAuthResult> LinkAsync(
            PlayServEpicAuthRequest request,
            CancellationToken ct = default);
    }
}
