using System.Threading;
using System.Threading.Tasks;
using Playserv.FacebookLogin;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.FacebookLogin.Samples
{
    public sealed class FacebookIdentityLoginSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public Task<PlayServAuthResult> LoginAsync()
        {
            // The game must complete FB.Init before this method is called.
            var request = new PlayServFacebookLoginRequest(
                new[] { "public_profile", "email" });
            return PlayServFacebookLogin.LoginAsync(
                request,
                PlayServExternalLoginMode.PreserveCurrentPlayer,
                _lifetime.Token);
        }

        public Task<PlayServAuthResult> LinkAsync() =>
            PlayServFacebookLogin.LinkAsync(
                new PlayServFacebookLoginRequest(),
                _lifetime.Token);

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
