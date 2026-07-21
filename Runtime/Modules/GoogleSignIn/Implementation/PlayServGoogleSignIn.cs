using System.Threading;
using System.Threading.Tasks;
using Playserv.GoogleSignIn;

namespace Playserv.Wrapper
{
    public static class PlayServGoogleSignIn
    {
        internal static readonly IPlayServGoogleSignInApi Api = new PlayServGoogleSignInApi();

        public static bool IsAvailable => Api.IsAvailable;

        public static PlayServGoogleSignInSettings Settings => Api.Settings;

        public static Task<PlayServGoogleSignInCredential> SignInAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return Api.SignInAsync(request, ct);
        }

        public static Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return Api.SignInSilentlyAsync(request, ct);
        }

        public static void SignOut()
        {
            Api.SignOut();
        }

        public static void Disconnect()
        {
            Api.Disconnect();
        }
    }
}
