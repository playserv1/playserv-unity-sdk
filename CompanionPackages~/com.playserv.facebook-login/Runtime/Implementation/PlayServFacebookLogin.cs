using System.Threading;
using System.Threading.Tasks;
using Playserv.FacebookLogin;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Acquires nonce-bound Facebook Limited Login tokens through an installed
    /// Meta Unity SDK and verifies them with PlayServ.
    /// </summary>
    public static class PlayServFacebookLogin
    {
        internal static readonly IPlayServFacebookLoginApi Api = new PlayServFacebookLoginApi();

        /// <summary>Whether a compatible Meta Unity SDK API is loaded.</summary>
        public static bool IsAvailable => Api.IsAvailable;

        /// <summary>Whether the game has completed Meta SDK initialization.</summary>
        public static bool IsInitialized => Api.IsInitialized;

        /// <summary>Runs Limited Login and returns an unverified credential.</summary>
        public static Task<PlayServFacebookCredential> GetCredentialAsync(
            PlayServFacebookLoginRequest request = null,
            CancellationToken ct = default) => Api.GetCredentialAsync(request, ct);

        /// <summary>Runs Limited Login and logs the player in through PlayServ.</summary>
        public static Task<PlayServAuthResult> LoginAsync(
            PlayServFacebookLoginRequest request = null,
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default) => Api.LoginAsync(request, mode, ct);

        /// <summary>Runs Limited Login and links Facebook to the managed player.</summary>
        public static Task<PlayServAuthResult> LinkAsync(
            PlayServFacebookLoginRequest request = null,
            CancellationToken ct = default) => Api.LinkAsync(request, ct);
    }
}
