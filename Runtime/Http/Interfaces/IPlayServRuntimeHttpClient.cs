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
    }
}
