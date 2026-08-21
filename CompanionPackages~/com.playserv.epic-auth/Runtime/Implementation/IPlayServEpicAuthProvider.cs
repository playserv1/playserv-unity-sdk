using System.Threading;
using System.Threading.Tasks;

namespace Playserv.EpicAuth
{
    internal interface IPlayServEpicAuthProvider
    {
        bool IsAvailable { get; }

        bool IsInitialized { get; }

        Task<PlayServEpicCredential> GetCredentialAsync(PlayServEpicAuthRequest request, CancellationToken ct);
    }
}
