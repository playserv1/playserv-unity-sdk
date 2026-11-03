using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.SteamAuth.Samples
{
    public sealed class SteamIdentityLoginSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public async Task<PlayServAuthResult> LoginAsync()
        {
            if (!PlayServSteamAuth.IsAvailable || !PlayServSteamAuth.IsInitialized)
                throw new PlayServSteamAuthException("login", "provider_unavailable", "Steam is not ready.");

            return await PlayServSteamAuth.LoginAsync(
                PlayServExternalLoginMode.PreserveCurrentPlayer,
                _lifetime.Token);
        }

        public Task<PlayServAuthResult> LinkAsync() =>
            PlayServSteamAuth.LinkAsync(_lifetime.Token);

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
