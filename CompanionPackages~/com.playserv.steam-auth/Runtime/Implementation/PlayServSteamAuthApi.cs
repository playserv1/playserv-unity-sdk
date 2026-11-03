using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Identity;
using Playserv.Wrapper;

namespace Playserv.SteamAuth
{
    public sealed class PlayServSteamAuthApi : IPlayServSteamAuthApi
    {
        private readonly IPlayServSteamAuthProvider _provider;
        private readonly Func<PlayServExternalIdentityProof, PlayServExternalLoginMode, CancellationToken, Task<PlayServAuthResult>> _login;
        private readonly Func<PlayServExternalIdentityProof, CancellationToken, Task<PlayServAuthResult>> _link;

        public PlayServSteamAuthApi()
            : this(
                new PlayServSteamworksNetProvider(),
                (proof, mode, ct) => PlayServAuth.LoginExternalAsync(proof, mode, ct),
                (proof, ct) => PlayServAuth.LinkIdentityAsync(proof, ct))
        {
        }

        internal PlayServSteamAuthApi(
            IPlayServSteamAuthProvider provider,
            Func<PlayServExternalIdentityProof, PlayServExternalLoginMode, CancellationToken, Task<PlayServAuthResult>> login,
            Func<PlayServExternalIdentityProof, CancellationToken, Task<PlayServAuthResult>> link)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _link = link ?? throw new ArgumentNullException(nameof(link));
        }

        public bool IsAvailable => _provider.IsAvailable;

        public bool IsInitialized => _provider.IsInitialized;

        public Task<PlayServSteamCredential> GetCredentialAsync(CancellationToken ct = default) =>
            _provider.GetCredentialAsync(ct);

        public async Task<PlayServAuthResult> LoginAsync(
            PlayServExternalLoginMode mode = PlayServExternalLoginMode.PreserveCurrentPlayer,
            CancellationToken ct = default)
        {
            using var credential = await GetCredentialAsync(ct).ConfigureAwait(false);
            if (!credential.TryCreateBackendProof(out var proof))
                throw new PlayServSteamAuthException("login", "credential_released", "Steam credential was released before use.");
            return await _login(proof, mode, ct).ConfigureAwait(false);
        }

        public async Task<PlayServAuthResult> LinkAsync(CancellationToken ct = default)
        {
            using var credential = await GetCredentialAsync(ct).ConfigureAwait(false);
            if (!credential.TryCreateBackendProof(out var proof))
                throw new PlayServSteamAuthException("link", "credential_released", "Steam credential was released before use.");
            return await _link(proof, ct).ConfigureAwait(false);
        }
    }
}
