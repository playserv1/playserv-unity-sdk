using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.SteamAuth
{
    public interface IPlayServSteamAuthApi
    {
        bool IsAvailable { get; }

        bool IsInitialized { get; }

        Task<PlayServSteamCredential> GetCredentialAsync(CancellationToken ct = default);

        Task<PlayServAuthResult> LoginAsync(
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default);

        Task<PlayServAuthResult> LinkAsync(CancellationToken ct = default);
    }
}
