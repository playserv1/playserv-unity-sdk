using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Identity;
using Playserv.Wrapper;

namespace Playserv.FacebookLogin
{
    public sealed class PlayServFacebookLoginApi : IPlayServFacebookLoginApi
    {
        private readonly IPlayServFacebookLoginProvider _provider;
        private readonly Func<PlayServExternalIdentityProof, PlayServExternalLoginMode, CancellationToken, Task<PlayServAuthResult>> _login;
        private readonly Func<PlayServExternalIdentityProof, CancellationToken, Task<PlayServAuthResult>> _link;

        public PlayServFacebookLoginApi()
            : this(
                new PlayServMetaUnityProvider(),
                (proof, mode, ct) => PlayServAuth.LoginExternalAsync(proof, mode, ct),
                (proof, ct) => PlayServAuth.LinkIdentityAsync(proof, ct))
        {
        }

        internal PlayServFacebookLoginApi(
            IPlayServFacebookLoginProvider provider,
            Func<PlayServExternalIdentityProof, PlayServExternalLoginMode, CancellationToken, Task<PlayServAuthResult>> login,
            Func<PlayServExternalIdentityProof, CancellationToken, Task<PlayServAuthResult>> link)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _link = link ?? throw new ArgumentNullException(nameof(link));
        }

        public bool IsAvailable => _provider.IsAvailable;

        public bool IsInitialized => _provider.IsInitialized;

        public Task<PlayServFacebookCredential> GetCredentialAsync(
            PlayServFacebookLoginRequest request = null,
            CancellationToken ct = default) =>
            _provider.GetCredentialAsync(request ?? new PlayServFacebookLoginRequest(), ct);

        public async Task<PlayServAuthResult> LoginAsync(
            PlayServFacebookLoginRequest request = null,
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default)
        {
            var credential = await GetCredentialAsync(request, ct).ConfigureAwait(false);
            if (!credential.TryCreateBackendProof(out var proof))
                throw new PlayServFacebookLoginException("login", "invalid_credential", "Meta returned an incomplete Limited Login credential.");
            return await _login(proof, mode, ct).ConfigureAwait(false);
        }

        public async Task<PlayServAuthResult> LinkAsync(
            PlayServFacebookLoginRequest request = null,
            CancellationToken ct = default)
        {
            var credential = await GetCredentialAsync(request, ct).ConfigureAwait(false);
            if (!credential.TryCreateBackendProof(out var proof))
                throw new PlayServFacebookLoginException("link", "invalid_credential", "Meta returned an incomplete Limited Login credential.");
            return await _link(proof, ct).ConfigureAwait(false);
        }
    }
}
