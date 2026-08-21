using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Identity;
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
        private readonly Func<PlayServSettings> _getSettings;
        private readonly Func<PlayServState> _getState;
        private readonly Func<string, CancellationToken, Task<bool>> _refreshLiveAuthorization;
        private readonly Action _disconnectTransport;
        private readonly Func<Task<bool>> _connectTransport;

        private PlayServPlayerSession _session;
        private CancellationTokenSource _refreshLoopCancellation;
        private Task _refreshLoopTask;
        private int _suppressTerminalCloseEvents;
        private int _terminalSessionLossStarted;
        private int _ignoreExpectedSessionMergedClose;
        private readonly object _pendingCloseGate = new object();
        private PlayServTransportCloseInfo _pendingTerminalClose;
        private bool _disposed;

        public PlayServPlayerAuthCoordinator(
            Func<PlayServSettings, IPlayServRuntimeHttpClient> createHttpClient,
            IPlayServPlayerSessionStore defaultSessionStore,
            Func<PlayServSettings> getSettings,
            Func<PlayServState> getState,
            Func<string, CancellationToken, Task<bool>> refreshLiveAuthorization,
            Action disconnectTransport,
            Func<Task<bool>> connectTransport)
        {
            _createHttpClient = createHttpClient ?? throw new ArgumentNullException(nameof(createHttpClient));
            _defaultSessionStore = defaultSessionStore ?? throw new ArgumentNullException(nameof(defaultSessionStore));
            _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
            _getState = getState ?? throw new ArgumentNullException(nameof(getState));
            _refreshLiveAuthorization = refreshLiveAuthorization ?? throw new ArgumentNullException(nameof(refreshLiveAuthorization));
            _disconnectTransport = disconnectTransport ?? throw new ArgumentNullException(nameof(disconnectTransport));
            _connectTransport = connectTransport ?? throw new ArgumentNullException(nameof(connectTransport));
        }

        public event Action<PlayServSessionLostInfo> SessionLost;

        public PlayServSessionInfo CurrentSession
        {
            get
            {
                var settings = TryGetSettings();
                if (_session != null && settings != null && ReferenceEquals(settings.RuntimeTokenProvider, _session))
                    return _session.CurrentSession;

                if (settings != null &&
                    (settings.RuntimeTokenProvider != null || !string.IsNullOrWhiteSpace(settings.PlayerAccessToken)))
                {
                    return new PlayServSessionInfo(settings.UserId, PlayServSessionKind.Unmanaged);
                }

                return new PlayServSessionInfo(string.Empty, PlayServSessionKind.None);
            }
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

            var session = EnsureManagedSession(settings);
            settings.RuntimeTokenProvider = session;
            await session.GetTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(session.PlayerId))
                throw new InvalidOperationException("PlayServ player authentication did not resolve a player ID.");

            settings.UserId = session.PlayerId;
            Volatile.Write(ref _terminalSessionLossStarted, 0);
            return settings;
        }

        public async Task<PlayServAuthProvidersResult> GetProvidersAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var settings = _getSettings();
            var invalid = ValidatePublicClientOperation(settings);
            if (invalid != null)
            {
                return new PlayServAuthProvidersResult(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Array.Empty<PlayServAuthProviderInfo>(),
                    invalid);
            }

            var store = settings.PlayerSessionStore ?? _defaultSessionStore;
            if (_session != null && _session.Matches(settings, store))
                return await _session.GetProvidersAsync(cancellationToken);

            using var discoverySession = new PlayServPlayerSession(
                settings,
                _createHttpClient(settings),
                store);
            return await discoverySession.GetProvidersAsync(cancellationToken);
        }

        public Task<PlayServAuthResult> LinkIdentityAsync(
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (proof == null)
                throw new ArgumentNullException(nameof(proof));
            return CoordinateSessionReplacementAsync(
                (session, ct) => session.LinkIdentityAsync(proof, ct),
                "provider link",
                discardPendingReasonOnSuccess: null,
                cancellationToken: cancellationToken);
        }

        public async Task<PlayServAuthResult> UnlinkIdentityAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(providerId))
                throw new ArgumentException("Identity provider ID is required.", nameof(providerId));

            var settings = _getSettings();
            var invalid = ValidateManagedOperation(settings);
            if (invalid != null)
                return FailedResult(invalid, CurrentSession, _getState() == PlayServState.Online);

            EnsureStableTransportState();
            var session = EnsureManagedSession(settings);
            settings.RuntimeTokenProvider = session;
            var wasOnline = _getState() == PlayServState.Online;
            StopRefreshLoop();
            BeginTerminalCloseSuppression();
            try
            {
                var result = await session.UnlinkIdentityAsync(providerId.Trim(), cancellationToken);
                settings.UserId = session.PlayerId;
                if (result.Error?.Exception is PlayServSessionRejectedException rejected)
                {
                    await HandleTerminalSessionLossAsync(
                        rejected.Reason,
                        rejected.Message,
                        rejected.PreviousSession,
                        ignoreSuppression: true);
                    return WithTransport(result, transportReady: false);
                }

                var transportReady = !wasOnline;
                if (wasOnline && !string.IsNullOrWhiteSpace(session.CurrentAccessToken))
                {
                    try
                    {
                        transportReady = await _refreshLiveAuthorization(
                            session.CurrentAccessToken,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        transportReady = false;
                    }

                    if (!transportReady)
                    {
                        try
                        {
                            transportReady = await ReconnectForChangedSessionAsync(
                                settings,
                                wasOnline: true,
                                cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            if (result.IsSuccess)
                            {
                                return FailedResult(
                                    CreateSessionRefreshError(exception),
                                    session.CurrentSession,
                                    transportReady: false,
                                    PlayServIdentityMutationState.Applied);
                            }
                            transportReady = false;
                        }
                    }
                }

                if (wasOnline && !transportReady && result.IsSuccess)
                {
                    return FailedResult(
                        CreateSessionRefreshError(null),
                        session.CurrentSession,
                        transportReady: false,
                        PlayServIdentityMutationState.Applied);
                }

                if (wasOnline && transportReady)
                    HandleConnected(settings);
                return WithTransport(result, wasOnline && transportReady);
            }
            finally
            {
                EndTerminalCloseSuppression();
            }
        }

        public Task<PlayServAuthResult> MergeIdentityAsync(
            PlayServAuthConflict conflict,
            PlayServMergeChoice choice,
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (conflict == null)
                throw new ArgumentNullException(nameof(conflict));
            if (proof == null)
                throw new ArgumentNullException(nameof(proof));
            return CoordinateSessionReplacementAsync(
                (session, ct) => session.MergeIdentityAsync(conflict, choice, proof, ct),
                "identity merge",
                discardPendingReasonOnSuccess: "SessionMerged",
                cancellationToken: cancellationToken);
        }

        public async Task<PlayServAuthResult> LoginExternalAsync(
            PlayServExternalIdentityProof proof,
            PlayServExternalLoginMode mode,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (proof == null)
                throw new ArgumentNullException(nameof(proof));

            var settings = _getSettings();
            var invalid = ValidateManagedOperation(settings);
            if (invalid != null)
                return FailedResult(invalid, CurrentSession, _getState() == PlayServState.Online);

            EnsureStableTransportState();
            var session = EnsureManagedSession(settings);
            settings.RuntimeTokenProvider = session;
            var wasOnline = _getState() == PlayServState.Online;
            var previousAccessToken = session.CurrentAccessToken;
            StopRefreshLoop();
            BeginTerminalCloseSuppression();
            try
            {
                var result = await session.LoginExternalAsync(proof, mode, cancellationToken);
                settings.UserId = session.PlayerId;

                if (result.Error?.Exception is PlayServSessionRejectedException rejected)
                {
                    await HandleTerminalSessionLossAsync(
                        rejected.Reason,
                        rejected.Message,
                        rejected.PreviousSession,
                        ignoreSuppression: true);
                    return WithTransport(result, transportReady: false);
                }

                if (!result.IsSuccess)
                {
                    var transportReady = wasOnline;
                    var accessTokenChanged = !string.Equals(
                        previousAccessToken,
                        session.CurrentAccessToken,
                        StringComparison.Ordinal);
                    if (wasOnline && accessTokenChanged && !string.IsNullOrWhiteSpace(session.CurrentAccessToken))
                    {
                        try
                        {
                            transportReady = await _refreshLiveAuthorization(
                                session.CurrentAccessToken,
                                cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            transportReady = false;
                        }
                    }

                    if (wasOnline && !transportReady)
                    {
                        try
                        {
                            transportReady = await ReconnectForChangedSessionAsync(
                                settings,
                                wasOnline: true,
                                cancellationToken: cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            PlayServLog.Warning(
                                PlayServLogCategory.General,
                                $"[PlayServ][auth] Failed to restore the transport after an unsuccessful login: {exception.Message}");
                            transportReady = false;
                        }
                    }
                    else if (wasOnline)
                    {
                        HandleConnected(settings);
                    }
                    return WithTransport(result, transportReady);
                }

                Volatile.Write(ref _terminalSessionLossStarted, 0);
                bool reconnected;
                try
                {
                    reconnected = await ReconnectForChangedSessionAsync(settings, wasOnline, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return FailedResult(
                        CreateTransportReconnectError(exception),
                        session.CurrentSession,
                        transportReady: false);
                }

                if (wasOnline && !reconnected)
                {
                    return FailedResult(
                        CreateTransportReconnectError(null),
                        session.CurrentSession,
                        transportReady: false);
                }
                return WithTransport(result, reconnected);
            }
            finally
            {
                EndTerminalCloseSuppression();
            }
        }

        private async Task<PlayServAuthResult> CoordinateSessionReplacementAsync(
            Func<PlayServPlayerSession, CancellationToken, Task<PlayServAuthResult>> operation,
            string operationLabel,
            string discardPendingReasonOnSuccess,
            CancellationToken cancellationToken)
        {
            var settings = _getSettings();
            var invalid = ValidateManagedOperation(settings);
            if (invalid != null)
                return FailedResult(invalid, CurrentSession, _getState() == PlayServState.Online);

            EnsureStableTransportState();
            var session = EnsureManagedSession(settings);
            settings.RuntimeTokenProvider = session;
            var wasOnline = _getState() == PlayServState.Online;
            var previousAccessToken = session.CurrentAccessToken;
            var operationSucceeded = false;
            StopRefreshLoop();
            BeginTerminalCloseSuppression();
            try
            {
                var result = await operation(session, cancellationToken);
                settings.UserId = session.PlayerId;
                if (result.Error?.Exception is PlayServSessionRejectedException rejected)
                {
                    await HandleTerminalSessionLossAsync(
                        rejected.Reason,
                        rejected.Message,
                        rejected.PreviousSession,
                        ignoreSuppression: true);
                    return WithTransport(result, transportReady: false);
                }

                if (!result.IsSuccess)
                {
                    var transportReady = wasOnline;
                    var accessTokenChanged = !string.Equals(
                        previousAccessToken,
                        session.CurrentAccessToken,
                        StringComparison.Ordinal);
                    if (wasOnline && accessTokenChanged && !string.IsNullOrWhiteSpace(session.CurrentAccessToken))
                    {
                        try
                        {
                            transportReady = await _refreshLiveAuthorization(
                                session.CurrentAccessToken,
                                cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            transportReady = false;
                        }
                    }

                    if (wasOnline && !transportReady && session.CurrentSession.IsLoggedIn)
                    {
                        try
                        {
                            transportReady = await ReconnectForChangedSessionAsync(
                                settings,
                                wasOnline: true,
                                cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            PlayServLog.Warning(
                                PlayServLogCategory.General,
                                $"[PlayServ][auth] Failed to restore transport after {operationLabel}: {exception.Message}");
                            transportReady = false;
                        }
                    }
                    else if (wasOnline && transportReady)
                    {
                        HandleConnected(settings);
                    }
                    return WithTransport(result, wasOnline && transportReady);
                }

                operationSucceeded = true;
                if (string.Equals(
                        discardPendingReasonOnSuccess,
                        "SessionMerged",
                        StringComparison.Ordinal))
                {
                    ArmExpectedSessionMergedClose();
                }
                Volatile.Write(ref _terminalSessionLossStarted, 0);
                bool reconnected;
                try
                {
                    reconnected = await ReconnectForChangedSessionAsync(settings, wasOnline, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return FailedResult(
                        CreateTransportReconnectError(exception),
                        session.CurrentSession,
                        transportReady: false,
                        result.IdentityMutationState);
                }

                if (wasOnline && !reconnected)
                {
                    return FailedResult(
                        CreateTransportReconnectError(null),
                        session.CurrentSession,
                        transportReady: false,
                        result.IdentityMutationState);
                }
                return WithTransport(result, reconnected);
            }
            finally
            {
                EndTerminalCloseSuppression(
                    operationSucceeded ? discardPendingReasonOnSuccess : null);
            }
        }

        public async Task<PlayServAuthResult> LogoutAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var settings = _getSettings();
            var invalid = ValidateManagedOperation(settings);
            if (invalid != null)
                return FailedResult(invalid, CurrentSession, _getState() == PlayServState.Online);

            EnsureStableTransportState();
            var session = EnsureManagedSession(settings);
            settings.RuntimeTokenProvider = session;
            var wasOnline = _getState() == PlayServState.Online;
            StopRefreshLoop();
            BeginTerminalCloseSuppression();
            try
            {
                if (wasOnline)
                    _disconnectTransport();

                var result = await session.LogoutAsync(cancellationToken);
                settings.UserId = session.PlayerId;
                if (session.CurrentSession.IsLoggedIn)
                    Volatile.Write(ref _terminalSessionLossStarted, 0);
                var reconnected = false;
                if (wasOnline && session.CurrentSession.IsLoggedIn)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        reconnected = await _connectTransport();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        return FailedResult(
                            CreateTransportReconnectError(exception),
                            session.CurrentSession,
                            transportReady: false);
                    }
                }

                if (wasOnline && session.CurrentSession.IsLoggedIn && !reconnected && result.IsSuccess)
                {
                    return FailedResult(
                        CreateTransportReconnectError(null),
                        session.CurrentSession,
                        transportReady: false);
                }
                return WithTransport(result, reconnected);
            }
            finally
            {
                EndTerminalCloseSuppression(discardAll: true);
            }
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

        public void HandleTransportClosed(PlayServTransportCloseInfo closeInfo)
        {
            if (_disposed ||
                closeInfo == null ||
                !TryMapCloseReason(closeInfo.Reason, out var reason))
            {
                return;
            }

            if (Volatile.Read(ref _suppressTerminalCloseEvents) > 0)
            {
                lock (_pendingCloseGate)
                {
                    if (_pendingTerminalClose == null ||
                        string.Equals(closeInfo.Reason, "Banned", StringComparison.Ordinal))
                    {
                        _pendingTerminalClose = closeInfo;
                    }
                }
                return;
            }

            if (reason == PlayServSessionLostReason.SessionMerged &&
                Interlocked.Exchange(ref _ignoreExpectedSessionMergedClose, 0) != 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _terminalSessionLossStarted, 1) != 0)
                return;

            StopRefreshLoop();
            _disconnectTransport();
            _ = HandleTerminalSessionLossAsync(
                reason,
                closeInfo.Reason,
                previousSession: null,
                transportAlreadyDisconnected: true,
                lossGuardAcquired: true);
        }

        private void BeginTerminalCloseSuppression()
        {
            if (Interlocked.Increment(ref _suppressTerminalCloseEvents) != 1)
                return;
            lock (_pendingCloseGate)
                _pendingTerminalClose = null;
        }

        private void EndTerminalCloseSuppression(
            string discardReason = null,
            bool discardAll = false)
        {
            if (Interlocked.Decrement(ref _suppressTerminalCloseEvents) != 0)
                return;

            PlayServTransportCloseInfo pending;
            lock (_pendingCloseGate)
            {
                pending = _pendingTerminalClose;
                _pendingTerminalClose = null;
            }

            var discardedExpectedMerge = pending != null &&
                !string.IsNullOrWhiteSpace(discardReason) &&
                string.Equals(pending.Reason, discardReason, StringComparison.Ordinal);
            if (discardedExpectedMerge)
                Interlocked.Exchange(ref _ignoreExpectedSessionMergedClose, 0);

            if (pending == null || discardAll || discardedExpectedMerge)
            {
                return;
            }
            HandleTransportClosed(pending);
        }

        private void ArmExpectedSessionMergedClose()
        {
            Interlocked.Exchange(ref _ignoreExpectedSessionMergedClose, 1);
            _ = ClearExpectedSessionMergedCloseAsync();
        }

        private async Task ClearExpectedSessionMergedCloseAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            Interlocked.Exchange(ref _ignoreExpectedSessionMergedClose, 0);
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
            Interlocked.Exchange(ref _ignoreExpectedSessionMergedClose, 0);
            StopRefreshLoop();
            _session?.Dispose();
            _session = null;
        }

        private PlayServPlayerSession EnsureManagedSession(PlayServSettings settings)
        {
            var sessionStore = settings.PlayerSessionStore ?? _defaultSessionStore;
            if (_session != null && _session.Matches(settings, sessionStore))
                return _session;

            ReleaseAutomaticSession(settings);
            _session = new PlayServPlayerSession(settings, _createHttpClient(settings), sessionStore);
            return _session;
        }

        private async Task<bool> ReconnectForChangedSessionAsync(
            PlayServSettings settings,
            bool wasOnline,
            CancellationToken cancellationToken)
        {
            if (!wasOnline)
                return false;

            cancellationToken.ThrowIfCancellationRequested();
            _disconnectTransport();
            var connected = await _connectTransport();
            cancellationToken.ThrowIfCancellationRequested();
            return connected;
        }

        private async Task RunRefreshLoopAsync(PlayServPlayerSession session, CancellationToken cancellationToken)
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
                    catch (PlayServSessionRejectedException exception)
                    {
                        await HandleTerminalSessionLossAsync(
                            exception.Reason,
                            exception.Message,
                            exception.PreviousSession);
                        return;
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

        private async Task HandleTerminalSessionLossAsync(
            PlayServSessionLostReason reason,
            string message,
            PlayServSessionInfo previousSession = null,
            bool transportAlreadyDisconnected = false,
            bool lossGuardAcquired = false,
            bool ignoreSuppression = false)
        {
            if (_disposed || (!ignoreSuppression && Volatile.Read(ref _suppressTerminalCloseEvents) > 0))
                return;

            if (!lossGuardAcquired && Interlocked.Exchange(ref _terminalSessionLossStarted, 1) != 0)
                return;

            StopRefreshLoop();
            if (_session != null)
            {
                try
                {
                    previousSession = previousSession ?? await _session.InvalidateAsync();
                }
                catch (Exception exception)
                {
                    PlayServLog.Warning(
                        PlayServLogCategory.General,
                        $"[PlayServ][auth] Failed to clear a lost player session: {exception.Message}");
                    previousSession = previousSession ?? _session.CurrentSession;
                }
            }

            if (!transportAlreadyDisconnected)
                _disconnectTransport();
            SessionLost?.Invoke(new PlayServSessionLostInfo(
                reason,
                previousSession ?? new PlayServSessionInfo(string.Empty, PlayServSessionKind.None),
                message,
                CanRetry(reason)));
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

        private PlayServAuthError ValidateManagedOperation(PlayServSettings settings)
        {
            var publicClientError = ValidatePublicClientOperation(settings);
            if (publicClientError != null)
                return publicClientError;

            if (!string.IsNullOrWhiteSpace(settings.PlayerAccessToken) ||
                (settings.RuntimeTokenProvider != null && !ReferenceEquals(settings.RuntimeTokenProvider, _session)))
            {
                return PlayServPlayerSession.CreateAuthError(
                    PlayServAuthErrorCode.InvalidConfiguration,
                    "PlayServAuth cannot replace an application-managed runtime credential provider.",
                    null);
            }

            return null;
        }

        private static PlayServAuthError ValidatePublicClientOperation(PlayServSettings settings)
        {
            if (settings == null ||
                string.IsNullOrWhiteSpace(settings.ClientToken) ||
                string.IsNullOrWhiteSpace(settings.BackendServerAddress))
            {
                return PlayServPlayerSession.CreateAuthError(
                    PlayServAuthErrorCode.InvalidConfiguration,
                    "Configure a public ClientToken and BackendServerAddress before using PlayServAuth.",
                    null);
            }

            if (!settings.ClientToken.Trim().StartsWith("pk_", StringComparison.Ordinal))
            {
                return PlayServPlayerSession.CreateAuthError(
                    PlayServAuthErrorCode.InvalidConfiguration,
                    "PlayServAuth requires a public pk_* ClientToken.",
                    null);
            }
            return null;
        }

        private void EnsureStableTransportState()
        {
            var state = _getState();
            if (state == PlayServState.Connecting ||
                state == PlayServState.Handshaking ||
                state == PlayServState.Reconnecting)
            {
                throw new InvalidOperationException(
                    "PlayServAuth operations require the transport to be Offline or Online.");
            }
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

        private PlayServSettings TryGetSettings()
        {
            try
            {
                return _getSettings();
            }
            catch
            {
                return null;
            }
        }

        private static PlayServAuthResult WithTransport(PlayServAuthResult result, bool transportReady)
        {
            return new PlayServAuthResult(
                result.Status,
                result.Session,
                result.Error,
                result.Conflict,
                transportReady,
                result.IdentityMutationState);
        }

        private static PlayServAuthResult FailedResult(
            PlayServAuthError error,
            PlayServSessionInfo session,
            bool transportReady,
            PlayServIdentityMutationState mutationState = PlayServIdentityMutationState.None)
        {
            return new PlayServAuthResult(
                PlayServAuthOperationStatus.Failed,
                session,
                error,
                conflict: null,
                transportReady,
                mutationState);
        }

        private static PlayServAuthError CreateTransportReconnectError(Exception exception)
        {
            return PlayServPlayerSession.CreateAuthError(
                PlayServAuthErrorCode.TransportReconnectFailed,
                exception?.Message ?? "The player session changed, but the PlayServ transport could not reconnect.",
                exception);
        }

        private static PlayServAuthError CreateSessionRefreshError(Exception exception)
        {
            return PlayServPlayerSession.CreateAuthError(
                PlayServAuthErrorCode.SessionRefreshFailed,
                exception?.Message ?? "The provider was unlinked, but the updated authorization could not be applied to the transport.",
                exception);
        }

        private static bool TryMapCloseReason(string value, out PlayServSessionLostReason reason)
        {
            switch (value?.Trim())
            {
                case "SessionRevoked":
                    reason = PlayServSessionLostReason.SessionRevoked;
                    return true;
                case "SessionMerged":
                    reason = PlayServSessionLostReason.SessionMerged;
                    return true;
                case "Banned":
                    reason = PlayServSessionLostReason.Banned;
                    return true;
                case "EnvMismatch":
                    reason = PlayServSessionLostReason.EnvironmentMismatch;
                    return true;
                case "ExpiredNoRefresh":
                    reason = PlayServSessionLostReason.ExpiredWithoutRefresh;
                    return true;
                case "CredentialRevoked":
                    reason = PlayServSessionLostReason.CredentialRevoked;
                    return true;
                default:
                    reason = PlayServSessionLostReason.Unknown;
                    return false;
            }
        }

        private static bool CanRetry(PlayServSessionLostReason reason)
        {
            return reason != PlayServSessionLostReason.Banned &&
                   reason != PlayServSessionLostReason.CredentialRevoked;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlayServPlayerAuthCoordinator));
        }
    }
}
