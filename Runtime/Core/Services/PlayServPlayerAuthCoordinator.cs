using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    internal sealed class PlayServPlayerAuthCoordinator : IDisposable
    {
        private static readonly TimeSpan OfflinePollDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RefreshRetryDelay = TimeSpan.FromSeconds(15);

        private readonly Func<PlayServSettings, IPlayServRuntimeHttpClient> _createHttpClient;
        private readonly IPlayServPlayerSessionStore _defaultSessionStore;
        private readonly Func<PlayServState> _getState;
        private readonly Func<string, CancellationToken, Task<bool>> _refreshLiveAuthorization;

        private PlayServAnonymousPlayerSession _session;
        private CancellationTokenSource _refreshLoopCancellation;
        private Task _refreshLoopTask;
        private bool _disposed;

        public PlayServPlayerAuthCoordinator(
            Func<PlayServSettings, IPlayServRuntimeHttpClient> createHttpClient,
            IPlayServPlayerSessionStore defaultSessionStore,
            Func<PlayServState> getState,
            Func<string, CancellationToken, Task<bool>> refreshLiveAuthorization)
        {
            _createHttpClient = createHttpClient ?? throw new ArgumentNullException(nameof(createHttpClient));
            _defaultSessionStore = defaultSessionStore ?? throw new ArgumentNullException(nameof(defaultSessionStore));
            _getState = getState ?? throw new ArgumentNullException(nameof(getState));
            _refreshLiveAuthorization = refreshLiveAuthorization ?? throw new ArgumentNullException(nameof(refreshLiveAuthorization));
        }

        public async Task<PlayServSettings> PrepareSettingsForConnectAsync(
            PlayServSettings settings,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!ShouldUseAutomaticAuthentication(settings))
            {
                ReleaseAutomaticSession(settings);
                return settings;
            }

            var sessionStore = settings.PlayerSessionStore ?? _defaultSessionStore;
            if (_session == null || !_session.Matches(settings, sessionStore))
            {
                ReleaseAutomaticSession(settings);
                _session = new PlayServAnonymousPlayerSession(
                    settings,
                    _createHttpClient(settings),
                    sessionStore);
            }

            settings.RuntimeTokenProvider = _session;
            await _session.GetTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(_session.PlayerId))
                throw new InvalidOperationException("PlayServ player authentication did not resolve a player ID.");

            settings.UserId = _session.PlayerId;
            return settings;
        }

        public void HandleConnected(PlayServSettings settings)
        {
            ThrowIfDisposed();
            StopRefreshLoop();
            if (_session == null || settings == null || !ReferenceEquals(settings.RuntimeTokenProvider, _session))
                return;

            _refreshLoopCancellation = new CancellationTokenSource();
            _refreshLoopTask = RunRefreshLoopAsync(_session, _refreshLoopCancellation.Token);
        }

        public void StopRefreshLoop()
        {
            if (_refreshLoopCancellation == null)
                return;

            _refreshLoopCancellation.Cancel();
            _refreshLoopCancellation.Dispose();
            _refreshLoopCancellation = null;
            _refreshLoopTask = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopRefreshLoop();
            _session?.Dispose();
            _session = null;
        }

        private bool ShouldUseAutomaticAuthentication(PlayServSettings settings)
        {
            if (!settings.EnableAutomaticPlayerAuthentication ||
                string.IsNullOrWhiteSpace(settings.ClientToken) ||
                !string.IsNullOrWhiteSpace(settings.PlayerAccessToken))
            {
                return false;
            }

            return settings.RuntimeTokenProvider == null || ReferenceEquals(settings.RuntimeTokenProvider, _session);
        }

        private void ReleaseAutomaticSession(PlayServSettings settings)
        {
            StopRefreshLoop();
            if (_session == null)
                return;

            if (settings != null && ReferenceEquals(settings.RuntimeTokenProvider, _session))
                settings.RuntimeTokenProvider = null;

            _session.Dispose();
            _session = null;
        }

        private async Task RunRefreshLoopAsync(
            PlayServAnonymousPlayerSession session,
            CancellationToken cancellationToken)
        {
            string pendingAccessToken = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(pendingAccessToken))
                        {
                            await Task.Delay(session.GetDelayUntilRefresh(), cancellationToken);
                            pendingAccessToken = await session.RotateAccessTokenAsync(cancellationToken);
                        }

                        if (_getState() != PlayServState.Online)
                        {
                            pendingAccessToken = null;
                            await Task.Delay(OfflinePollDelay, cancellationToken);
                            continue;
                        }

                        if (await _refreshLiveAuthorization(pendingAccessToken, cancellationToken))
                        {
                            pendingAccessToken = null;
                            PlayServLog.Trace(PlayServLogCategory.General, "[PlayServ][auth] Live player authorization updated.");
                            continue;
                        }

                        PlayServLog.Warning(
                            PlayServLogCategory.General,
                            "[PlayServ][auth] Server rejected the live authorization update; retrying.");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        pendingAccessToken = null;
                        PlayServLog.Warning(
                            PlayServLogCategory.General,
                            $"[PlayServ][auth] Player token refresh failed: {exception.Message}");
                    }

                    await Task.Delay(RefreshRetryDelay, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when the SDK disconnects or changes authentication mode.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlayServPlayerAuthCoordinator));
        }
    }
}
