using System.Threading;
using System.Threading.Tasks;
using Playserv.SteamAuth;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Acquires short-lived Steam Web API tickets through an installed
    /// Steamworks.NET runtime and verifies them with PlayServ.
    /// </summary>
    public static class PlayServSteamAuth
    {
        internal static readonly IPlayServSteamAuthApi Api = new PlayServSteamAuthApi();

        /// <summary>Whether a compatible Steamworks.NET API is loaded.</summary>
        public static bool IsAvailable => Api.IsAvailable;

        /// <summary>Whether Steam is running and the local user is logged on.</summary>
        public static bool IsInitialized => Api.IsInitialized;

        /// <summary>Acquires a disposable, unverified Steam Web API credential.</summary>
        public static Task<PlayServSteamCredential> GetCredentialAsync(CancellationToken ct = default) =>
            Api.GetCredentialAsync(ct);

        /// <summary>Acquires a fresh ticket and logs the player in through PlayServ.</summary>
        public static Task<PlayServAuthResult> LoginAsync(
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default) => Api.LoginAsync(mode, ct);

        /// <summary>Acquires a fresh ticket and links Steam to the managed player.</summary>
        public static Task<PlayServAuthResult> LinkAsync(CancellationToken ct = default) =>
            Api.LinkAsync(ct);
    }
}
