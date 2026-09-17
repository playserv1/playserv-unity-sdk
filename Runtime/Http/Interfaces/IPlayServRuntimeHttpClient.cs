using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Http.Interfaces
{
    public interface IPlayServRuntimeHttpClient
    {
        Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default);

        /// <summary>
        /// Mints a fresh anonymous player session through <c>POST /auth/players/anon</c>.
        /// </summary>
        Task<PlayerTokenBundleDto> SignInAnonAsync(
            string clientToken,
            CancellationToken ct = default);

        /// <summary>
        /// Rotates an existing player session through <c>POST /auth/players/refresh</c>.
        /// </summary>
        Task<PlayerRefreshResponseDto> RefreshAsync(
            string clientToken,
            string refreshToken,
            CancellationToken ct = default);

        Task<PlayerTokenBundleDto> LoginExternalAsync(
            string clientToken,
            PlayerExternalLoginRequestDto request,
            string playerAccessToken = null,
            CancellationToken ct = default);

        Task SignOutAsync(
            string clientToken,
            string refreshToken,
            CancellationToken ct = default);

        Task<PlayServRuntimeDataResponse> SendDataAsync(
            PlayServRuntimeDataRequest request,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Optional additive capability for anonymous authentication with a game-selected display
    /// name and fingerprint. The name uses the literal <c>display_name</c> JSON key; trim and cap
    /// at 64 UTF-16 code units without splitting surrogate pairs, omitting blank names.
    /// Existing HTTP modules keep working without this capability (the name is then omitted).
    /// </summary>
    public interface IPlayServAnonymousLoginHttpClient
    {
        Task<PlayerTokenBundleDto> SignInAnonAsync(
            string clientToken,
            PlayerAnonymousLoginRequestDto request,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Optional additive capability for exact byte uploads, bounded byte responses and
    /// streaming downloads. Existing custom HTTP modules do not need to implement it.
    /// </summary>
    public interface IPlayServRuntimeBinaryHttpClient
    {
        Task<PlayServRuntimeDataResponse> SendBinaryDataAsync(
            PlayServRuntimeBinaryDataRequest request,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Optional extension implemented by runtime HTTP clients that support the
    /// managed player identity lifecycle introduced in SDK 0.3.8.
    /// </summary>
    public interface IPlayServPlayerIdentityHttpClient
    {
        Task<PlayerTokenBundleDto> SignInAnonAsync(
            string clientToken,
            PlayerFingerprintDto fingerprint,
            CancellationToken ct = default);

        Task<PlayerAuthProvidersProbeDto> GetAuthProvidersAsync(
            string clientToken,
            CancellationToken ct = default);

        Task<PlayerTokenBundleDto> LinkIdentityAsync(
            string clientToken,
            PlayerLinkRequestDto request,
            string playerAccessToken,
            CancellationToken ct = default);

        Task UnlinkIdentityAsync(
            string clientToken,
            PlayerUnlinkRequestDto request,
            string playerAccessToken,
            CancellationToken ct = default);

        Task<PlayerTokenBundleDto> MergeIdentityAsync(
            string clientToken,
            PlayerMergeRequestDto request,
            string playerAccessToken,
            CancellationToken ct = default);
    }
}
