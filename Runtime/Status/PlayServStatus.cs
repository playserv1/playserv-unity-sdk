using System.Threading;
using System.Threading.Tasks;
using Playserv.Status;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Credential-free access to PlayServ's public platform health surface.
    /// </summary>
    public static class PlayServStatus
    {
        /// <summary>Returns public function/deployment health for a project, optionally filtered by environment.</summary>
        public static Task<PlayServProjectStatus> GetProjectAsync(
            string projectSlug, string environment = null, CancellationToken cancellationToken = default) =>
            PlayServStatusClient.CreateDefault().GetProjectAsync(projectSlug, environment, cancellationToken);

        /// <summary>Returns the current status of every reported PoP/system pair.</summary>
        public static Task<PlayServPlatformStatus> GetCurrentAsync(
            CancellationToken cancellationToken = default) =>
            PlayServStatusClient.CreateDefault().GetCurrentAsync(cancellationToken);

        /// <summary>Returns daily availability history for a PoP.</summary>
        /// <param name="pop">Optional PoP ID. When omitted, the backend selects its configured PoP.</param>
        /// <param name="days">History window from 1 through 365 days.</param>
        public static Task<PlayServPlatformStatusHistory> GetHistoryAsync(
            string pop = null,
            int days = 90,
            CancellationToken cancellationToken = default) =>
            PlayServStatusClient.CreateDefault().GetHistoryAsync(pop, days, cancellationToken);

        /// <summary>
        /// Returns peer status origins advertised by the current cluster. The SDK
        /// validates but never contacts these origins automatically.
        /// </summary>
        public static Task<PlayServStatusFederation> GetFederationAsync(
            CancellationToken cancellationToken = default) =>
            PlayServStatusClient.CreateDefault().GetFederationAsync(cancellationToken);
    }
}
