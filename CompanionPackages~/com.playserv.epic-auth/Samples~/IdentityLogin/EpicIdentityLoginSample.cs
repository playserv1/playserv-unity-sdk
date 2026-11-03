using System.Threading;
using System.Threading.Tasks;
using Playserv.EpicAuth;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.EpicAuth.Samples
{
    public sealed class EpicIdentityLoginSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public Task<PlayServAuthResult> LoginEasAsync() =>
            PlayServEpicAuth.LoginAsync(
                PlayServEpicAuthRequest.Eos(),
                PlayServExternalLoginMode.PreserveCurrentPlayer,
                _lifetime.Token);

        public Task<PlayServAuthResult> LoginFromLauncherAsync() =>
            PlayServEpicAuth.LoginAsync(
                PlayServEpicAuthRequest.Launcher(),
                PlayServExternalLoginMode.PreserveCurrentPlayer,
                _lifetime.Token);

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
