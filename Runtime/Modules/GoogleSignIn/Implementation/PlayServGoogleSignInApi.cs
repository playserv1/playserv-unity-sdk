using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GoogleSignIn
{
    public sealed class PlayServGoogleSignInApi : IPlayServGoogleSignInApi
    {
        public bool IsAvailable => PlayServGoogleSignInPluginBridge.IsAvailable;

        public PlayServGoogleSignInSettings Settings => PlayServGoogleSignInSettingsProvider.LoadOrDefault();

        public Task<PlayServGoogleSignInCredential> SignInAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync("sign in", request, silent: false, ct);
        }

        public Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync("silent sign in", request, silent: true, ct);
        }

        public void SignOut()
        {
            var bridge = RequireBridge("sign out");
            bridge.SignOut();
        }

        public void Disconnect()
        {
            var bridge = RequireBridge("disconnect");
            bridge.Disconnect();
        }

        private async Task<PlayServGoogleSignInCredential> ExecuteAsync(
            string operation,
            PlayServGoogleSignInRequest request,
            bool silent,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var bridge = RequireBridge(operation);
            return silent
                ? await bridge.SignInSilentlyAsync(Settings, request, ct).ConfigureAwait(false)
                : await bridge.SignInAsync(Settings, request, ct).ConfigureAwait(false);
        }

        private static PlayServGoogleSignInPluginBridge RequireBridge(string operation)
        {
            if (PlayServGoogleSignInPluginBridge.TryCreate(out var bridge))
                return bridge;

            throw new PlayServGoogleSignInException(operation, PlayServGoogleSignInPluginBridge.ProviderMissingMessage);
        }
    }
}
