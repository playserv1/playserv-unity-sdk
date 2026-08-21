using System.Threading;
using System.Threading.Tasks;
using Playserv.EpicAuth;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Acquires Epic Account Services or launcher credentials through an
    /// installed EOS-Contrib runtime and verifies them with PlayServ.
    /// </summary>
    public static class PlayServEpicAuth
    {
        internal static readonly IPlayServEpicAuthApi Api = new PlayServEpicAuthApi();

        /// <summary>Whether a compatible EOS-Contrib API is loaded.</summary>
        public static bool IsAvailable => Api.IsAvailable;

        /// <summary>Whether the EOS Auth interface is initialized.</summary>
        public static bool IsInitialized => Api.IsInitialized;

        /// <summary>Acquires an unverified Epic credential for the requested source.</summary>
        public static Task<PlayServEpicCredential> GetCredentialAsync(
            PlayServEpicAuthRequest request,
            CancellationToken ct = default) => Api.GetCredentialAsync(request, ct);

        /// <summary>Acquires an Epic credential and logs the player in through PlayServ.</summary>
        public static Task<PlayServAuthResult> LoginAsync(
            PlayServEpicAuthRequest request,
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default) => Api.LoginAsync(request, mode, ct);

        /// <summary>Acquires an Epic credential and links it to the managed player.</summary>
        public static Task<PlayServAuthResult> LinkAsync(
            PlayServEpicAuthRequest request,
            CancellationToken ct = default) => Api.LinkAsync(request, ct);
    }
}
