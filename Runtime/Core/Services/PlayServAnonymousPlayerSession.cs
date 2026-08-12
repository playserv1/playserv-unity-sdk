using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    internal sealed class PlayServAnonymousPlayerSession : IPlayServRuntimeTokenProvider, IDisposable
    {
        private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(90);

        private readonly IPlayServRuntimeHttpClient _httpClient;
        private readonly IPlayServPlayerSessionStore _sessionStore;
        private readonly string _clientToken;
        private readonly SynchronizationContext _unityContext;
        private readonly int _unityThreadId;
        private readonly SemaphoreSlim _tokenGate = new SemaphoreSlim(1, 1);

        private string _accessToken = string.Empty;
        private string _refreshToken = string.Empty;
        private DateTimeOffset _accessTokenExpiresAtUtc;
        private bool _storeLoaded;
        private bool _disposed;

        public PlayServAnonymousPlayerSession(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient httpClient,
            IPlayServPlayerSessionStore sessionStore,
            SynchronizationContext unityContext = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _clientToken = PlayServCredentialPolicy.NormalizeClientToken(settings.ClientToken)
                ?? throw new InvalidOperationException(
                    "A public PlayServ client token is required for automatic player authentication.");
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _unityContext = unityContext ?? SynchronizationContext.Current;
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
            ScopeKey = BuildScopeKey(settings);
        }

        public string ScopeKey { get; }

        public string PlayerId { get; private set; } = string.Empty;

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return RunOnUnityThreadAsync(
                () => ResolveAccessTokenAsync(forceRefresh: false, cancellationToken),
                cancellationToken);
        }

        public Task<string> RotateAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return RunOnUnityThreadAsync(
                () => ResolveAccessTokenAsync(forceRefresh: true, cancellationToken),
                cancellationToken);
        }

        public TimeSpan GetDelayUntilRefresh()
        {
            var delay = _accessTokenExpiresAtUtc - DateTimeOffset.UtcNow - RefreshSkew;
            return delay > TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1);
        }

        public bool Matches(
            PlayServSettings settings,
            IPlayServPlayerSessionStore sessionStore)
        {
            return settings != null &&
                   ReferenceEquals(_sessionStore, sessionStore) &&
                   ScopeKey == BuildScopeKey(settings) &&
                   string.Equals(_clientToken, settings.ClientToken?.Trim(), StringComparison.Ordinal);
        }

        public static string BuildScopeKey(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var endpoint = settings.BackendServerAddress?.Trim() ?? string.Empty;
            var authority = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                ? uri.Authority
                : endpoint;
            return $"{settings.GameId?.Trim()}.{authority}";
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        private async Task<string> ResolveAccessTokenAsync(
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            await _tokenGate.WaitAsync(cancellationToken);
            try
            {
                await EnsureStoredSessionLoadedAsync(cancellationToken);

                if (!forceRefresh && IsAccessTokenFresh())
                    return _accessToken;

                if (!string.IsNullOrWhiteSpace(_refreshToken) && !string.IsNullOrWhiteSpace(PlayerId))
                {
                    try
                    {
                        var refreshed = await _httpClient.RefreshAsync(
                            _clientToken,
                            _refreshToken,
                            cancellationToken);
                        await ApplyRefreshedSessionAsync(refreshed, cancellationToken);
                        return _accessToken;
                    }
                    catch (InvalidOperationException exception) when (IsUnauthorized(exception))
                    {
                        await ClearStoredSessionAsync(cancellationToken);
                    }
                }

                var bundle = await _httpClient.SignInAnonAsync(_clientToken, cancellationToken);
                await ApplyAnonymousSessionAsync(bundle, cancellationToken);
                return _accessToken;
            }
            finally
            {
                _tokenGate.Release();
            }
        }

        private async Task EnsureStoredSessionLoadedAsync(CancellationToken cancellationToken)
        {
            if (_storeLoaded)
                return;

            var stored = await _sessionStore.LoadAsync(ScopeKey, cancellationToken);
            _storeLoaded = true;
            if (stored == null ||
                string.IsNullOrWhiteSpace(stored.PlayerId) ||
                string.IsNullOrWhiteSpace(stored.RefreshToken))
            {
                return;
            }

            PlayerId = stored.PlayerId.Trim();
            _refreshToken = stored.RefreshToken.Trim();
        }

        private bool IsAccessTokenFresh()
        {
            return !string.IsNullOrWhiteSpace(_accessToken) &&
                   _accessTokenExpiresAtUtc - DateTimeOffset.UtcNow > RefreshSkew;
        }

        private async Task ApplyAnonymousSessionAsync(
            PlayerTokenBundleDto bundle,
            CancellationToken cancellationToken)
        {
            if (bundle == null ||
                string.IsNullOrWhiteSpace(bundle.player_id) ||
                string.IsNullOrWhiteSpace(bundle.access_token) ||
                string.IsNullOrWhiteSpace(bundle.refresh_token) ||
                bundle.expires_in <= 0)
            {
                throw new InvalidOperationException("PlayServ returned an incomplete anonymous player session.");
            }

            var issuedAt = ParseServerTimestamp(bundle.issued_at, "issued_at");
            PlayerId = bundle.player_id.Trim();
            _accessToken = bundle.access_token.Trim();
            _refreshToken = bundle.refresh_token.Trim();
            _accessTokenExpiresAtUtc = issuedAt.AddSeconds(bundle.expires_in);
            await SaveSessionAsync(cancellationToken);
        }

        private async Task ApplyRefreshedSessionAsync(
            PlayerRefreshResponseDto refreshed,
            CancellationToken cancellationToken)
        {
            if (refreshed == null ||
                string.IsNullOrWhiteSpace(refreshed.access_token) ||
                string.IsNullOrWhiteSpace(refreshed.refresh_token))
            {
                throw new InvalidOperationException("PlayServ returned an incomplete refreshed player session.");
            }

            _accessToken = refreshed.access_token.Trim();
            _refreshToken = refreshed.refresh_token.Trim();
            _accessTokenExpiresAtUtc = ParseServerTimestamp(refreshed.expires_at, "expires_at");
            await SaveSessionAsync(cancellationToken);
        }

        private Task SaveSessionAsync(CancellationToken cancellationToken)
        {
            return _sessionStore.SaveAsync(
                ScopeKey,
                new PlayServPlayerSessionData(PlayerId, _refreshToken),
                cancellationToken);
        }

        private async Task ClearStoredSessionAsync(CancellationToken cancellationToken)
        {
            _accessToken = string.Empty;
            _refreshToken = string.Empty;
            _accessTokenExpiresAtUtc = default;
            PlayerId = string.Empty;
            await _sessionStore.ClearAsync(ScopeKey, cancellationToken);
        }

        private Task<T> RunOnUnityThreadAsync<T>(
            Func<Task<T>> action,
            CancellationToken cancellationToken)
        {
            if (_unityContext == null || Thread.CurrentThread.ManagedThreadId == _unityThreadId)
                return action();

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _unityContext.Post(async _ =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(await action());
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, null);
            return completion.Task;
        }

        private static DateTimeOffset ParseServerTimestamp(string value, string fieldName)
        {
            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                return timestamp;
            }

            throw new InvalidOperationException($"PlayServ returned an invalid {fieldName} timestamp.");
        }

        private static bool IsUnauthorized(InvalidOperationException exception)
        {
            return exception.Message.IndexOf("HTTP 401", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlayServAnonymousPlayerSession));
        }
    }
}
