using System.Threading;
using System.Threading.Tasks;

namespace Playserv.SteamAuth
{
    internal interface IPlayServSteamAuthProvider
    {
        bool IsAvailable { get; }

        bool IsInitialized { get; }

        Task<PlayServSteamCredential> GetCredentialAsync(CancellationToken ct);
    }
}
