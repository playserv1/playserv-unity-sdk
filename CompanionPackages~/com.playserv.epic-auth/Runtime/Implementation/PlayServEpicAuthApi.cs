using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Identity;
using Playserv.Wrapper;

namespace Playserv.EpicAuth
{
    public sealed class PlayServEpicAuthApi : IPlayServEpicAuthApi
    {
        private readonly IPlayServEpicAuthProvider _provider;
        private readonly Func<PlayServExternalIdentityProof, PlayServExternalLoginMode, CancellationToken, Task<PlayServAuthResult>> _login;
        private readonly Func<PlayServExternalIdentityProof, CancellationToken, Task<PlayServAuthResult>> _link;

        public PlayServEpicAuthApi()
            : this(
                new PlayServEosContribProvider(),
                (proof, mode, ct) => PlayServAuth.LoginExternalAsync(proof, mode, ct),
                (proof, ct) => PlayServAuth.LinkIdentityAsync(proof, ct))
        {
        }

        internal PlayServEpicAuthApi(
            IPlayServEpicAuthProvider provider,
            Func<PlayServExternalIdentityProof, PlayServExternalLoginMode, CancellationToken, Task<PlayServAuthResult>> login,
            Func<PlayServExternalIdentityProof, CancellationToken, Task<PlayServAuthResult>> link)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _link = link ?? throw new ArgumentNullException(nameof(link));
        }

        public bool IsAvailable => _provider.IsAvailable;

        public bool IsInitialized => _provider.IsInitialized;

        public Task<PlayServEpicCredential> GetCredentialAsync(
            PlayServEpicAuthRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            return _provider.GetCredentialAsync(request, ct);
        }

        public async Task<PlayServAuthResult> LoginAsync(
            PlayServEpicAuthRequest request,
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default)
        {
            var credential = await GetCredentialAsync(request, ct).ConfigureAwait(false);
            if (!credential.TryCreateBackendProof(out var proof))
                throw new PlayServEpicAuthException("login", "invalid_credential", "EOS returned an empty credential.");
            return await _login(proof, mode, ct).ConfigureAwait(false);
        }

        public async Task<PlayServAuthResult> LinkAsync(
            PlayServEpicAuthRequest request,
            CancellationToken ct = default)
        {
            var credential = await GetCredentialAsync(request, ct).ConfigureAwait(false);
            if (!credential.TryCreateBackendProof(out var proof))
                throw new PlayServEpicAuthException("link", "invalid_credential", "EOS returned an empty credential.");
            return await _link(proof, ct).ConfigureAwait(false);
        }
    }
}
