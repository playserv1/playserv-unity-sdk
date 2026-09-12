using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Identity;

namespace Playserv.Wrapper
{
    internal interface IPlayServAuthApi
    {
        bool IsLoggedIn { get; }

        string PlayerId { get; }

        PlayServSessionKind SessionKind { get; }

        PlayServSessionInfo CurrentSession { get; }

        PlayServPlayerProfile CurrentPlayerProfile { get; }

        event Action<PlayServSessionLostInfo> SessionLost;

        Task<PlayServAuthProvidersResult> GetProvidersAsync(
            CancellationToken cancellationToken = default);

        Task<PlayServPlayerProfileResult> GetCurrentPlayerProfileAsync(
            CancellationToken cancellationToken = default);

        Task<PlayServPlayerProfileResult> RefreshCurrentPlayerProfileAsync(
            CancellationToken cancellationToken = default);

        Task<PlayServAuthResult> LoginExternalAsync(
            PlayServExternalIdentityProof proof,
            PlayServExternalLoginMode mode,
            CancellationToken cancellationToken = default);

        Task<PlayServAuthResult> LogoutAsync(CancellationToken cancellationToken = default);

        Task<PlayServAuthResult> LinkIdentityAsync(
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default);

        Task<PlayServAuthResult> UnlinkIdentityAsync(
            string providerId,
            CancellationToken cancellationToken = default);

        Task<PlayServAuthResult> MergeIdentityAsync(
            PlayServAuthConflict conflict,
            PlayServMergeChoice choice,
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default);
    }
}
