using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GoogleSignIn
{
    internal sealed class PlayServGoogleSignInPluginProvider : IPlayServGoogleSignInProvider
    {
        public bool IsAvailable => PlayServGoogleSignInPluginBridge.IsAvailable;

        public Task<PlayServGoogleSignInCredential> SignInAsync(
            PlayServGoogleSignInSettings settings,
            PlayServGoogleSignInRequest request,
            CancellationToken ct)
        {
            return RequireBridge("sign in").SignInAsync(settings, request, ct);
        }

        public Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
            PlayServGoogleSignInSettings settings,
            PlayServGoogleSignInRequest request,
            CancellationToken ct)
        {
            return RequireBridge("silent sign in").SignInSilentlyAsync(settings, request, ct);
        }

        public void SignOut()
        {
            RequireBridge("sign out").SignOut();
        }

        public void Disconnect()
        {
            RequireBridge("disconnect").Disconnect();
        }

        private static PlayServGoogleSignInPluginBridge RequireBridge(string operation)
        {
            if (PlayServGoogleSignInPluginBridge.TryCreate(out var bridge))
                return bridge;

            throw new PlayServGoogleSignInException(
                operation,
                PlayServGoogleSignInPluginBridge.ProviderMissingMessage);
        }
    }
}
