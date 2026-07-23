using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GoogleSignIn
{
    public sealed class PlayServGoogleSignInApi : IPlayServGoogleSignInApi
    {
        private readonly IPlayServGoogleSignInProvider _provider;
        private readonly Func<PlayServGoogleSignInSettings> _loadSettings;

        public PlayServGoogleSignInApi()
            : this(
                new PlayServGoogleSignInPluginProvider(),
                PlayServGoogleSignInSettingsProvider.LoadOrDefault)
        {
        }

        internal PlayServGoogleSignInApi(
            IPlayServGoogleSignInProvider provider,
            Func<PlayServGoogleSignInSettings> loadSettings)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        }

        public bool IsAvailable => _provider.IsAvailable;

        public PlayServGoogleSignInSettings Settings => _loadSettings();

        public Task<PlayServGoogleSignInCredential> SignInAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(request, silent: false, ct);
        }

        public Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
            PlayServGoogleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(request, silent: true, ct);
        }

        public void SignOut()
        {
            _provider.SignOut();
        }

        public void Disconnect()
        {
            _provider.Disconnect();
        }

        private async Task<PlayServGoogleSignInCredential> ExecuteAsync(
            PlayServGoogleSignInRequest request,
            bool silent,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return silent
                ? await _provider.SignInSilentlyAsync(Settings, request, ct).ConfigureAwait(false)
                : await _provider.SignInAsync(Settings, request, ct).ConfigureAwait(false);
        }
    }
}
