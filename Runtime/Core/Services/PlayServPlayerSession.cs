using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Identity;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal sealed class PlayServPlayerSession : IPlayServRuntimeTokenProvider, IDisposable
    {
        private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(90);
        private static readonly HashSet<string> ProviderTokenIds = new HashSet<string>(StringComparer.Ordinal)
        {
            PlayServIdentityProviderIds.Apple,
            PlayServIdentityProviderIds.Google,
            PlayServIdentityProviderIds.Facebook,
            PlayServIdentityProviderIds.Epic,
            PlayServIdentityProviderIds.Steam,
            PlayServIdentityProviderIds.PlayServToken
        };
        private static int _defaultStoreWarningLogged;

        private readonly IPlayServRuntimeHttpClient _httpClient;
        private readonly IPlayServPlayerSessionStore _sessionStore;
        private readonly IPlayServPlayerFingerprintProvider _configuredFingerprintProvider;
        private readonly IPlayServPlayerFingerprintProvider _fingerprintProvider;
        private readonly bool _automaticFingerprintEnabled;
        private readonly string _clientToken;
        private readonly SynchronizationContext _unityContext;
        private readonly int _unityThreadId;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);

        private string _accessToken = string.Empty;
        private string _refreshToken = string.Empty;
        private DateTimeOffset _accessTokenExpiresAtUtc;
        private DateTimeOffset? _refreshTokenExpiresAtUtc;
        private PlayServSessionKind _sessionKind = PlayServSessionKind.None;
        private string[] _linkedProviders = Array.Empty<string>();
        private bool _areLinkedProvidersKnown;
        private bool _storeLoaded;
        private int _generation;
        private bool _disposed;

        public PlayServPlayerSession(
            PlayServSettings settings,
            IPlayServRuntimeHttpClient httpClient,
            IPlayServPlayerSessionStore sessionStore,
            SynchronizationContext unityContext = null,
            IPlayServUnityDeviceInfo unityDeviceInfo = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _clientToken = PlayServCredentialPolicy.NormalizeClientToken(settings.ClientToken)
                ?? throw new InvalidOperationException(
                    "A public PlayServ client token is required for automatic player authentication.");
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _configuredFingerprintProvider = settings.PlayerFingerprintProvider;
            _automaticFingerprintEnabled = settings.EnableAutomaticPlayerFingerprint;
            _fingerprintProvider = _configuredFingerprintProvider ??
                (_automaticFingerprintEnabled
                    ? new PlayServAutomaticPlayerFingerprintProvider(unityDeviceInfo)
                    : null);
            _unityContext = unityContext ?? SynchronizationContext.Current;
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
            ScopeKey = BuildScopeKey(settings);
        }

        public string ScopeKey { get; }

        public string PlayerId { get; private set; } = string.Empty;

        public PlayServSessionKind SessionKind => _sessionKind;

        internal string CurrentAccessToken => _accessToken;

        public PlayServSessionInfo CurrentSession => new PlayServSessionInfo(
            PlayerId,
            _sessionKind,
            _linkedProviders,
            _areLinkedProvidersKnown);

        public Task<PlayServAuthProvidersResult> GetProvidersAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return RunOnUnityThreadAsync(
                () => GetProvidersCoreAsync(cancellationToken),
                cancellationToken);
        }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return RunOnUnityThreadAsync(
                () => ResolveAccessTokenAsync(forceRefresh: false, allowAnonymousFallback: true, cancellationToken),
                cancellationToken);
        }

        public Task<string> RotateAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return RunOnUnityThreadAsync(
                () => ResolveAccessTokenAsync(forceRefresh: true, allowAnonymousFallback: false, cancellationToken),
                cancellationToken);
        }

        public Task<PlayServAuthResult> LoginExternalAsync(
            PlayServExternalIdentityProof proof,
            PlayServExternalLoginMode mode,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (proof == null)
                throw new ArgumentNullException(nameof(proof));

            if (!Enum.IsDefined(typeof(PlayServExternalLoginMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));

            return RunOnUnityThreadAsync(
                () => LoginExternalCoreAsync(proof, mode, cancellationToken),
                cancellationToken);
        }

        public Task<PlayServAuthResult> LogoutAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return RunOnUnityThreadAsync(
                () => LogoutCoreAsync(cancellationToken),
                cancellationToken);
        }

        public Task<PlayServAuthResult> LinkIdentityAsync(
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (proof == null)
                throw new ArgumentNullException(nameof(proof));
            return RunOnUnityThreadAsync(
                () => LinkIdentityCoreAsync(proof, cancellationToken),
                cancellationToken);
        }

        public Task<PlayServAuthResult> UnlinkIdentityAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(providerId))
                throw new ArgumentException("Identity provider ID is required.", nameof(providerId));
            return RunOnUnityThreadAsync(
                () => UnlinkIdentityCoreAsync(providerId.Trim(), cancellationToken),
                cancellationToken);
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
            if (!Enum.IsDefined(typeof(PlayServMergeChoice), choice))
                throw new ArgumentOutOfRangeException(nameof(choice));
            if (conflict.Kind != PlayServAuthConflictKind.ProviderAlreadyLinked ||
                conflict.Current == null || conflict.Conflicting == null)
            {
                throw new ArgumentException(
                    "Merge requires a provider conflict containing current and conflicting player snapshots.",
                    nameof(conflict));
            }
            if (!string.Equals(conflict.ProviderId, proof.ProviderId, StringComparison.Ordinal))
                throw new ArgumentException("Merge proof provider must match the auth conflict provider.", nameof(proof));

            return RunOnUnityThreadAsync(
                () => MergeIdentityCoreAsync(conflict, choice, proof, cancellationToken),
                cancellationToken);
        }

        public Task<PlayServSessionInfo> InvalidateAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return RunOnUnityThreadAsync(async () =>
            {
                await _operationGate.WaitAsync(cancellationToken);
                try
                {
                    var previous = CurrentSession;
                    Interlocked.Increment(ref _generation);
                    await ClearStoredSessionAsync(cancellationToken);
                    return previous;
                }
                finally
                {
                    _operationGate.Release();
                }
            }, cancellationToken);
        }

        public TimeSpan GetDelayUntilRefresh()
        {
            var delay = _accessTokenExpiresAtUtc - DateTimeOffset.UtcNow - RefreshSkew;
            return delay > TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1);
        }

        public bool Matches(PlayServSettings settings, IPlayServPlayerSessionStore sessionStore)
        {
            return settings != null &&
                   ReferenceEquals(_sessionStore, sessionStore) &&
                   ReferenceEquals(_configuredFingerprintProvider, settings.PlayerFingerprintProvider) &&
                   _automaticFingerprintEnabled == settings.EnableAutomaticPlayerFingerprint &&
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
            Interlocked.Increment(ref _generation);
        }

        private async Task<string> ResolveAccessTokenAsync(
            bool forceRefresh,
            bool allowAnonymousFallback,
            CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                await EnsureStoredSessionLoadedAsync(cancellationToken);
                var operationGeneration = Interlocked.Increment(ref _generation);

                if (!forceRefresh && IsAccessTokenFresh())
                    return _accessToken;

                if (!string.IsNullOrWhiteSpace(_refreshToken) && !string.IsNullOrWhiteSpace(PlayerId))
                {
                    if (_refreshTokenExpiresAtUtc.HasValue &&
                        _refreshTokenExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
                    {
                        var expiredSession = CurrentSession;
                        ThrowIfOperationBecameStale(operationGeneration);
                        await ClearStoredSessionAsync(cancellationToken);
                        if (!allowAnonymousFallback)
                            throw new PlayServSessionRejectedException(
                                expiredSession,
                                PlayServSessionLostReason.ExpiredWithoutRefresh,
                                "The player refresh token has expired.");
                    }
                    else
                    {
                        try
                        {
                            var refreshed = await _httpClient.RefreshAsync(
                                _clientToken,
                                _refreshToken,
                                cancellationToken);
                            if (!IsOperationCurrent(operationGeneration))
                            {
                                await TrySignOutAsync(refreshed.refresh_token, cancellationToken);
                                throw new PlayServStaleSessionOperationException();
                            }
                            try
                            {
                                await ApplyRefreshedSessionAsync(refreshed, cancellationToken);
                            }
                            catch (PlayServSessionPersistenceException)
                            {
                                await TrySignOutAsync(refreshed.refresh_token, cancellationToken);
                                throw;
                            }
                            return _accessToken;
                        }
                        catch (PlayServRuntimeHttpException exception) when (IsTerminalAuthFailure(exception))
                        {
                            var rejectedSession = CurrentSession;
                            ThrowIfOperationBecameStale(operationGeneration);
                            await ClearStoredSessionAsync(cancellationToken);
                            if (!allowAnonymousFallback)
                                throw new PlayServSessionRejectedException(
                                    rejectedSession,
                                    PlayServSessionLostReason.RefreshRejected,
                                    exception.Message,
                                    exception);
                        }
                    }
                }

                if (!allowAnonymousFallback)
                    throw new PlayServSessionRejectedException(
                        CurrentSession,
                        PlayServSessionLostReason.RefreshRejected,
                        "No refreshable PlayServ player session is available.");

                var bundle = await SignInAnonymousAsync(cancellationToken);
                if (!IsOperationCurrent(operationGeneration))
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    throw new PlayServStaleSessionOperationException();
                }
                try
                {
                    await ApplyTokenBundleAsync(bundle, PlayServSessionKind.Anonymous, cancellationToken);
                }
                catch (PlayServSessionPersistenceException)
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    throw;
                }
                return _accessToken;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<PlayServAuthResult> LoginExternalCoreAsync(
            PlayServExternalIdentityProof proof,
            PlayServExternalLoginMode mode,
            CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var operationGeneration = 0;
                string authorization = null;
                PlayerTokenBundleDto bundle = null;
                try
                {
                    await EnsureStoredSessionLoadedAsync(cancellationToken);
                    operationGeneration = Interlocked.Increment(ref _generation);

                    var fingerprint = await CollectFingerprintAsync(cancellationToken);
                    if (mode == PlayServExternalLoginMode.PreserveCurrentPlayer)
                    {
                        authorization = await ResolveAccessTokenWithoutGateAsync(
                            forceRefresh: false,
                            allowAnonymousFallback: true,
                            operationGeneration,
                            cancellationToken,
                            fingerprint,
                            fingerprintWasCollected: true);
                    }

                    bundle = await _httpClient.LoginExternalAsync(
                        _clientToken,
                        new PlayerExternalLoginRequestDto
                        {
                            provider = proof.ProviderId,
                            provider_token = proof.ProviderToken,
                            mode = EmptyToNull(proof.Mode),
                            nonce = EmptyToNull(proof.Nonce),
                            fingerprint = fingerprint
                        },
                        authorization,
                        cancellationToken);
                }
                catch (PlayServRuntimeHttpException exception) when (exception.StatusCode == 409)
                {
                    return new PlayServAuthResult(
                        PlayServAuthOperationStatus.Conflict,
                        CurrentSession,
                        CreateAuthError(exception),
                        ParseConflict(exception.ResponseBody, proof.ProviderId),
                        transportReady: false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return FailedResult(CreateAuthError(exception));
                }

                if (!IsOperationCurrent(operationGeneration))
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    return FailedResult(CreateAuthError(
                        PlayServAuthErrorCode.Unknown,
                        "The login response became stale before it could be applied.",
                        null));
                }

                try
                {
                    await ApplyTokenBundleAsync(bundle, PlayServSessionKind.Registered, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    return FailedResult(CreateAuthError(
                        PlayServAuthErrorCode.PersistenceFailed,
                        "The verified player session could not be persisted.",
                        exception));
                }

                return new PlayServAuthResult(
                    PlayServAuthOperationStatus.Success,
                    CurrentSession,
                    error: null,
                    conflict: null,
                    transportReady: false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<PlayServAuthProvidersResult> GetProvidersCoreAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                var probe = await RequireIdentityHttpClient()
                    .GetAuthProvidersAsync(_clientToken, cancellationToken);
                if (probe?.project == null)
                    throw new InvalidOperationException("PlayServ returned an incomplete auth provider response.");

                var providers = (probe.providers ?? Array.Empty<PlayerAuthProviderAvailabilityDto>())
                    .Where(provider => provider != null)
                    .Select(provider => new PlayServAuthProviderInfo(
                        provider.id,
                        provider.label,
                        provider.enabled,
                        provider.connectivity,
                        provider.available,
                        ProviderTokenIds.Contains(provider.id ?? string.Empty)))
                    .ToArray();
                return new PlayServAuthProvidersResult(
                    probe.project.id,
                    probe.project.slug,
                    probe.project.env,
                    providers,
                    error: null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new PlayServAuthProvidersResult(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Array.Empty<PlayServAuthProviderInfo>(),
                    CreateAuthError(exception));
            }
        }

        private async Task<PlayServAuthResult> LinkIdentityCoreAsync(
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var operationGeneration = 0;
                PlayerTokenBundleDto bundle = null;
                try
                {
                    await EnsureStoredSessionLoadedAsync(cancellationToken);
                    operationGeneration = Interlocked.Increment(ref _generation);
                    var authorization = await ResolveAccessTokenWithoutGateAsync(
                        forceRefresh: false,
                        allowAnonymousFallback: true,
                        operationGeneration,
                        cancellationToken);
                    bundle = await RequireIdentityHttpClient().LinkIdentityAsync(
                        _clientToken,
                        new PlayerLinkRequestDto
                        {
                            provider = proof.ProviderId,
                            provider_token = proof.ProviderToken,
                            mode = EmptyToNull(proof.Mode),
                            nonce = EmptyToNull(proof.Nonce)
                        },
                        authorization,
                        cancellationToken);
                }
                catch (PlayServRuntimeHttpException exception) when (exception.StatusCode == 409)
                {
                    return new PlayServAuthResult(
                        PlayServAuthOperationStatus.Conflict,
                        CurrentSession,
                        CreateAuthError(exception),
                        ParseConflict(exception.ResponseBody, proof.ProviderId, exception.BackendCode),
                        transportReady: false,
                        PlayServIdentityMutationState.NotApplied);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return FailedResult(
                        CreateAuthError(exception),
                        MutationStateForRequestFailure(exception));
                }

                if (!IsOperationCurrent(operationGeneration))
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    return FailedResult(
                        CreateAuthError(
                            PlayServAuthErrorCode.Unknown,
                            "The provider-link response became stale before it could be applied.",
                            null),
                        PlayServIdentityMutationState.Applied);
                }

                try
                {
                    await ApplyTokenBundleAsync(bundle, PlayServSessionKind.Registered, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    return FailedResult(
                        CreateAuthError(
                            PlayServAuthErrorCode.PersistenceFailed,
                            "The linked player session could not be persisted.",
                            exception),
                        PlayServIdentityMutationState.Applied);
                }

                return new PlayServAuthResult(
                    PlayServAuthOperationStatus.Success,
                    CurrentSession,
                    error: null,
                    conflict: null,
                    transportReady: false,
                    PlayServIdentityMutationState.Applied);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<PlayServAuthResult> UnlinkIdentityCoreAsync(
            string providerId,
            CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var operationGeneration = 0;
                var remoteApplied = false;
                try
                {
                    await EnsureStoredSessionLoadedAsync(cancellationToken);
                    operationGeneration = Interlocked.Increment(ref _generation);
                    var authorization = await ResolveAccessTokenWithoutGateAsync(
                        forceRefresh: false,
                        allowAnonymousFallback: false,
                        operationGeneration,
                        cancellationToken);
                    await RequireIdentityHttpClient().UnlinkIdentityAsync(
                        _clientToken,
                        new PlayerUnlinkRequestDto { provider = providerId },
                        authorization,
                        cancellationToken);
                    remoteApplied = true;

                    await ResolveAccessTokenWithoutGateAsync(
                        forceRefresh: true,
                        allowAnonymousFallback: false,
                        operationGeneration,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (!remoteApplied)
                    {
                        return FailedResult(
                            CreateAuthError(exception),
                            MutationStateForRequestFailure(exception));
                    }

                    return FailedResult(
                        CreateAuthError(
                            PlayServAuthErrorCode.SessionRefreshFailed,
                            "The provider was unlinked, but the player session could not be refreshed.",
                            exception),
                        PlayServIdentityMutationState.Applied);
                }

                return new PlayServAuthResult(
                    PlayServAuthOperationStatus.Success,
                    CurrentSession,
                    error: null,
                    conflict: null,
                    transportReady: false,
                    PlayServIdentityMutationState.Applied);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<PlayServAuthResult> MergeIdentityCoreAsync(
            PlayServAuthConflict conflict,
            PlayServMergeChoice choice,
            PlayServExternalIdentityProof proof,
            CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                await EnsureStoredSessionLoadedAsync(cancellationToken);
                if (!string.Equals(PlayerId, conflict.Current.PlayerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The merge conflict no longer belongs to the current managed player session.");
                }

                var operationGeneration = Interlocked.Increment(ref _generation);
                var authorization = await ResolveAccessTokenWithoutGateAsync(
                    forceRefresh: false,
                    allowAnonymousFallback: false,
                    operationGeneration,
                    cancellationToken);
                var primaryPlayerId = choice == PlayServMergeChoice.KeepCurrentPlayer
                    ? conflict.Current.PlayerId
                    : conflict.Conflicting.PlayerId;
                var absorbedPlayerId = choice == PlayServMergeChoice.KeepCurrentPlayer
                    ? conflict.Conflicting.PlayerId
                    : conflict.Current.PlayerId;

                PlayerTokenBundleDto bundle;
                try
                {
                    bundle = await RequireIdentityHttpClient().MergeIdentityAsync(
                        _clientToken,
                        new PlayerMergeRequestDto
                        {
                            primary_plr_id = primaryPlayerId,
                            absorbed_plr_id = absorbedPlayerId,
                            provider = proof.ProviderId,
                            provider_token = proof.ProviderToken,
                            mode = EmptyToNull(proof.Mode),
                            nonce = EmptyToNull(proof.Nonce)
                        },
                        authorization,
                        cancellationToken);
                }
                catch (PlayServRuntimeHttpException exception) when (exception.StatusCode == 409)
                {
                    return new PlayServAuthResult(
                        PlayServAuthOperationStatus.Conflict,
                        CurrentSession,
                        CreateAuthError(exception),
                        ParseConflict(exception.ResponseBody, proof.ProviderId, exception.BackendCode),
                        transportReady: false,
                        PlayServIdentityMutationState.NotApplied);
                }
                catch (PlayServRuntimeHttpException exception) when (exception.StatusCode == 403)
                {
                    return await ResolveForbiddenMergeOutcomeAsync(
                        exception,
                        operationGeneration,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return FailedResult(
                        CreateAuthError(exception),
                        MutationStateForRequestFailure(exception));
                }

                if (!IsOperationCurrent(operationGeneration))
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    return FailedResult(
                        CreateAuthError(
                            PlayServAuthErrorCode.Unknown,
                            "The merge response became stale before it could be applied.",
                            null),
                        PlayServIdentityMutationState.Applied);
                }

                try
                {
                    await ApplyTokenBundleAsync(bundle, PlayServSessionKind.Registered, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await TryRevokeBundleAsync(bundle, cancellationToken);
                    return FailedResult(
                        CreateAuthError(
                            PlayServAuthErrorCode.PersistenceFailed,
                            "The merged primary-player session could not be persisted.",
                            exception),
                        PlayServIdentityMutationState.Applied);
                }

                return new PlayServAuthResult(
                    PlayServAuthOperationStatus.Success,
                    CurrentSession,
                    error: null,
                    conflict: null,
                    transportReady: false,
                    PlayServIdentityMutationState.Applied);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<PlayServAuthResult> ResolveForbiddenMergeOutcomeAsync(
            PlayServRuntimeHttpException mergeException,
            int operationGeneration,
            CancellationToken cancellationToken)
        {
            try
            {
                await ResolveAccessTokenWithoutGateAsync(
                    forceRefresh: true,
                    allowAnonymousFallback: false,
                    operationGeneration,
                    cancellationToken);
                return FailedResult(
                    CreateAuthError(mergeException),
                    PlayServIdentityMutationState.NotApplied);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServSessionRejectedException rejected)
            {
                var banned = new PlayServSessionRejectedException(
                    rejected.PreviousSession,
                    PlayServSessionLostReason.Banned,
                    "The identity merge committed and the primary player is banned.",
                    mergeException);
                return FailedResult(
                    CreateAuthError(
                        PlayServAuthErrorCode.DeviceBanned,
                        banned.Message,
                        banned),
                    PlayServIdentityMutationState.Applied);
            }
            catch (Exception refreshException)
            {
                return FailedResult(
                    CreateAuthError(
                        PlayServAuthErrorCode.MergeOutcomeUnknown,
                        "The merge returned HTTP 403 and its final server outcome could not be verified.",
                        refreshException),
                    PlayServIdentityMutationState.Unknown);
            }
        }

        private async Task<PlayServAuthResult> LogoutCoreAsync(CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                try
                {
                    await EnsureStoredSessionLoadedAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return FailedResult(CreateAuthError(
                        PlayServAuthErrorCode.PersistenceFailed,
                        "The existing player session could not be loaded.",
                        exception));
                }

                var operationGeneration = Interlocked.Increment(ref _generation);
                var previousRefreshToken = _refreshToken;
                PlayServAuthError remoteSignOutError = null;

                if (!string.IsNullOrWhiteSpace(previousRefreshToken))
                {
                    try
                    {
                        await _httpClient.SignOutAsync(_clientToken, previousRefreshToken, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        remoteSignOutError = CreateAuthError(
                            PlayServAuthErrorCode.RemoteSignOutFailed,
                            "The remote player session could not be revoked; local logout continued.",
                            exception);
                    }
                }

                if (!IsOperationCurrent(operationGeneration))
                {
                    return FailedResult(CreateAuthError(
                        PlayServAuthErrorCode.Unknown,
                        "The logout response became stale before it could be applied.",
                        null));
                }

                try
                {
                    await ClearStoredSessionAsync(cancellationToken);
                    var anonymous = await SignInAnonymousAsync(cancellationToken);
                    if (!IsOperationCurrent(operationGeneration))
                    {
                        await TryRevokeBundleAsync(anonymous, cancellationToken);
                        return FailedResult(CreateAuthError(
                            PlayServAuthErrorCode.Unknown,
                            "The anonymous replacement session became stale before it could be applied.",
                            null));
                    }
                    try
                    {
                        await ApplyTokenBundleAsync(anonymous, PlayServSessionKind.Anonymous, cancellationToken);
                    }
                    catch (PlayServSessionPersistenceException)
                    {
                        await TryRevokeBundleAsync(anonymous, cancellationToken);
                        throw;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return FailedResult(CreateAuthError(exception));
                }

                return new PlayServAuthResult(
                    remoteSignOutError == null
                        ? PlayServAuthOperationStatus.Success
                        : PlayServAuthOperationStatus.Failed,
                    CurrentSession,
                    remoteSignOutError,
                    conflict: null,
                    transportReady: false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<string> ResolveAccessTokenWithoutGateAsync(
            bool forceRefresh,
            bool allowAnonymousFallback,
            int operationGeneration,
            CancellationToken cancellationToken,
            PlayerFingerprintDto collectedFingerprint = null,
            bool fingerprintWasCollected = false)
        {
            if (!forceRefresh && IsAccessTokenFresh())
                return _accessToken;

            if (!string.IsNullOrWhiteSpace(_refreshToken) && !string.IsNullOrWhiteSpace(PlayerId))
            {
                if (_refreshTokenExpiresAtUtc.HasValue &&
                    _refreshTokenExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
                {
                    var expiredSession = CurrentSession;
                    ThrowIfOperationBecameStale(operationGeneration);
                    await ClearStoredSessionAsync(cancellationToken);
                    throw new PlayServSessionRejectedException(
                        expiredSession,
                        PlayServSessionLostReason.ExpiredWithoutRefresh,
                        "The player refresh token has expired.");
                }

                try
                {
                    var refreshed = await _httpClient.RefreshAsync(
                        _clientToken,
                        _refreshToken,
                        cancellationToken);
                    if (!IsOperationCurrent(operationGeneration))
                    {
                        await TrySignOutAsync(refreshed.refresh_token, cancellationToken);
                        throw new PlayServStaleSessionOperationException();
                    }
                    try
                    {
                        await ApplyRefreshedSessionAsync(refreshed, cancellationToken);
                    }
                    catch (PlayServSessionPersistenceException)
                    {
                        await TrySignOutAsync(refreshed.refresh_token, cancellationToken);
                        throw;
                    }
                    return _accessToken;
                }
                catch (PlayServRuntimeHttpException exception) when (IsTerminalAuthFailure(exception))
                {
                    var rejectedSession = CurrentSession;
                    ThrowIfOperationBecameStale(operationGeneration);
                    await ClearStoredSessionAsync(cancellationToken);
                    throw new PlayServSessionRejectedException(
                        rejectedSession,
                        PlayServSessionLostReason.RefreshRejected,
                        exception.Message,
                        exception);
                }
            }

            if (!allowAnonymousFallback)
                throw new PlayServSessionRejectedException(
                    CurrentSession,
                    PlayServSessionLostReason.RefreshRejected,
                    "No refreshable PlayServ player session is available.");

            var bundle = await SignInAnonymousAsync(
                cancellationToken,
                collectedFingerprint,
                fingerprintWasCollected);
            if (!IsOperationCurrent(operationGeneration))
            {
                await TryRevokeBundleAsync(bundle, cancellationToken);
                throw new PlayServStaleSessionOperationException();
            }
            try
            {
                await ApplyTokenBundleAsync(bundle, PlayServSessionKind.Anonymous, cancellationToken);
            }
            catch (PlayServSessionPersistenceException)
            {
                await TryRevokeBundleAsync(bundle, cancellationToken);
                throw;
            }
            return _accessToken;
        }

        private async Task EnsureStoredSessionLoadedAsync(CancellationToken cancellationToken)
        {
            if (_storeLoaded)
                return;

            PlayServPlayerSessionData stored;
            try
            {
                stored = await _sessionStore.LoadAsync(ScopeKey, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlayServSessionPersistenceException(
                    "The stored PlayServ player session could not be loaded.",
                    exception);
            }
            _storeLoaded = true;
            if (stored == null ||
                string.IsNullOrWhiteSpace(stored.PlayerId) ||
                string.IsNullOrWhiteSpace(stored.RefreshToken))
            {
                return;
            }

            PlayerId = stored.PlayerId.Trim();
            _refreshToken = stored.RefreshToken.Trim();
            _sessionKind = stored.SessionKind == PlayServSessionKind.None
                ? PlayServSessionKind.Anonymous
                : stored.SessionKind;
            _refreshTokenExpiresAtUtc = stored.RefreshTokenExpiresAtUtc;
        }

        private bool IsAccessTokenFresh()
        {
            return !string.IsNullOrWhiteSpace(_accessToken) &&
                   _accessTokenExpiresAtUtc - DateTimeOffset.UtcNow > RefreshSkew;
        }

        private async Task ApplyTokenBundleAsync(
            PlayerTokenBundleDto bundle,
            PlayServSessionKind kind,
            CancellationToken cancellationToken)
        {
            if (bundle == null ||
                string.IsNullOrWhiteSpace(bundle.player_id) ||
                string.IsNullOrWhiteSpace(bundle.access_token) ||
                string.IsNullOrWhiteSpace(bundle.refresh_token) ||
                bundle.expires_in <= 0)
            {
                throw new InvalidOperationException("PlayServ returned an incomplete player token bundle.");
            }

            var issuedAt = ParseServerTimestamp(bundle.issued_at, "issued_at");
            var refreshExpiry = bundle.refresh_expires_in > 0
                ? issuedAt.AddSeconds(bundle.refresh_expires_in)
                : (DateTimeOffset?)null;
            var candidate = new PlayServPlayerSessionData(
                bundle.player_id.Trim(),
                bundle.refresh_token.Trim(),
                kind,
                refreshExpiry);

            await SaveSessionAsync(candidate, cancellationToken);
            PlayerId = candidate.PlayerId;
            _accessToken = bundle.access_token.Trim();
            _refreshToken = candidate.RefreshToken;
            _accessTokenExpiresAtUtc = issuedAt.AddSeconds(bundle.expires_in);
            _refreshTokenExpiresAtUtc = candidate.RefreshTokenExpiresAtUtc;
            _sessionKind = kind;
            UpdateLinkedProviders(bundle.access_token);
            _storeLoaded = true;
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

            var accessExpiry = ParseServerTimestamp(refreshed.expires_at, "expires_at");
            var refreshExpiry = refreshed.refresh_expires_in > 0
                ? DateTimeOffset.UtcNow.AddSeconds(refreshed.refresh_expires_in)
                : _refreshTokenExpiresAtUtc;
            var candidate = new PlayServPlayerSessionData(
                PlayerId,
                refreshed.refresh_token.Trim(),
                _sessionKind == PlayServSessionKind.None ? PlayServSessionKind.Anonymous : _sessionKind,
                refreshExpiry);

            await SaveSessionAsync(candidate, cancellationToken);
            _accessToken = refreshed.access_token.Trim();
            _refreshToken = candidate.RefreshToken;
            _accessTokenExpiresAtUtc = accessExpiry;
            _refreshTokenExpiresAtUtc = candidate.RefreshTokenExpiresAtUtc;
            _sessionKind = candidate.SessionKind;
            UpdateLinkedProviders(refreshed.access_token);
        }

        private void UpdateLinkedProviders(string accessToken)
        {
            _linkedProviders = Array.Empty<string>();
            _areLinkedProvidersKnown = false;
            if (string.IsNullOrWhiteSpace(accessToken))
                return;

            try
            {
                var token = accessToken.Trim();
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring("Bearer ".Length).Trim();
                var segments = token.Split('.');
                if (segments.Length < 2)
                    return;

                var payload = segments[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var codec = PlayServJsonCompositionRoot.CreateDefaultJsonCodec();
                var document = codec.ParseToPlainValue(json);
                _linkedProviders = JsonResponseReader
                    .GetStringArrayValueIgnoreCase(document, "providers", codec)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                _areLinkedProvidersKnown = true;
            }
            catch
            {
                _linkedProviders = Array.Empty<string>();
                _areLinkedProvidersKnown = false;
            }
        }

        private async Task SaveSessionAsync(
            PlayServPlayerSessionData session,
            CancellationToken cancellationToken)
        {
            WarnAboutDefaultRegisteredStore(session.SessionKind);
            try
            {
                await _sessionStore.SaveAsync(ScopeKey, session, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlayServSessionPersistenceException(
                    "The PlayServ player session could not be persisted.",
                    exception);
            }
        }

        private async Task ClearStoredSessionAsync(CancellationToken cancellationToken)
        {
            ClearMemorySession();
            try
            {
                await _sessionStore.ClearAsync(ScopeKey, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlayServSessionPersistenceException(
                    "The stored PlayServ player session could not be cleared.",
                    exception);
            }
        }

        private void ClearMemorySession()
        {
            _accessToken = string.Empty;
            _refreshToken = string.Empty;
            _accessTokenExpiresAtUtc = default;
            _refreshTokenExpiresAtUtc = null;
            PlayerId = string.Empty;
            _sessionKind = PlayServSessionKind.None;
            _linkedProviders = Array.Empty<string>();
            _areLinkedProvidersKnown = false;
            _storeLoaded = true;
        }

        private async Task<PlayerTokenBundleDto> SignInAnonymousAsync(
            CancellationToken cancellationToken,
            PlayerFingerprintDto collectedFingerprint = null,
            bool fingerprintWasCollected = false)
        {
            var fingerprint = fingerprintWasCollected
                ? collectedFingerprint
                : await CollectFingerprintAsync(cancellationToken);
            if (fingerprint == null)
                return await _httpClient.SignInAnonAsync(_clientToken, cancellationToken);
            return await RequireIdentityHttpClient()
                .SignInAnonAsync(_clientToken, fingerprint, cancellationToken);
        }

        private IPlayServPlayerIdentityHttpClient RequireIdentityHttpClient()
        {
            return _httpClient as IPlayServPlayerIdentityHttpClient ??
                   throw new InvalidOperationException(
                       "The configured runtime HTTP client does not support PlayServ player identity lifecycle operations.");
        }

        private async Task<PlayerFingerprintDto> CollectFingerprintAsync(
            CancellationToken cancellationToken)
        {
            if (_fingerprintProvider == null)
                return null;

            PlayServPlayerFingerprint fingerprint;
            try
            {
                fingerprint = await _fingerprintProvider.GetFingerprintAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlayServFingerprintProviderException(
                    "The configured player fingerprint provider failed before authentication.",
                    exception);
            }

            if (fingerprint == null)
                return null;

            return new PlayerFingerprintDto
            {
                stable = CopyFingerprintValues(fingerprint.Stable),
                soft = fingerprint.Soft.Count == 0
                    ? null
                    : CopyFingerprintValues(fingerprint.Soft)
            };
        }

        private static Dictionary<string, object> CopyFingerprintValues(
            IReadOnlyDictionary<string, object> values)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                result[pair.Key] = pair.Value is IReadOnlyDictionary<string, object> nested
                    ? new Dictionary<string, object>(nested, StringComparer.Ordinal)
                    : pair.Value;
            }
            return result;
        }

        private async Task TryRevokeBundleAsync(
            PlayerTokenBundleDto bundle,
            CancellationToken cancellationToken)
        {
            if (bundle == null || string.IsNullOrWhiteSpace(bundle.refresh_token))
                return;

            await TrySignOutAsync(bundle.refresh_token, cancellationToken);
        }

        private async Task TrySignOutAsync(string refreshToken, CancellationToken cancellationToken)
        {
            try
            {
                await _httpClient.SignOutAsync(_clientToken, refreshToken, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PlayServLog.Warning(
                    PlayServLogCategory.General,
                    $"[PlayServ][auth] Previous player session cleanup failed: {exception.Message}");
            }
        }

        private PlayServAuthResult FailedResult(
            PlayServAuthError error,
            PlayServIdentityMutationState mutationState = PlayServIdentityMutationState.None)
        {
            return new PlayServAuthResult(
                PlayServAuthOperationStatus.Failed,
                CurrentSession,
                error,
                conflict: null,
                transportReady: false,
                mutationState);
        }

        private static PlayServAuthConflict ParseConflict(
            string responseBody,
            string fallbackProvider,
            string backendCode = null)
        {
            var fallbackKind = string.Equals(
                backendCode,
                "merge_provider_conflict",
                StringComparison.OrdinalIgnoreCase)
                ? PlayServAuthConflictKind.MergeProviderConflict
                : PlayServAuthConflictKind.ProviderAlreadyLinked;
            if (string.IsNullOrWhiteSpace(responseBody))
                return new PlayServAuthConflict(fallbackProvider, null, null, fallbackKind);

            try
            {
                var dto = PlayServJsonCompositionRoot
                    .CreateDefaultJsonCodec()
                    .Deserialize<PlayerAuthConflictDto>(responseBody);
                return new PlayServAuthConflict(
                    string.IsNullOrWhiteSpace(dto?.provider) ? fallbackProvider : dto.provider,
                    ToConflictParty(dto?.current),
                    ToConflictParty(dto?.conflicting),
                    string.Equals(dto?.error, "merge_provider_conflict", StringComparison.OrdinalIgnoreCase) ||
                    fallbackKind == PlayServAuthConflictKind.MergeProviderConflict
                        ? PlayServAuthConflictKind.MergeProviderConflict
                        : PlayServAuthConflictKind.ProviderAlreadyLinked,
                    dto?.providers);
            }
            catch
            {
                return new PlayServAuthConflict(fallbackProvider, null, null, fallbackKind);
            }
        }

        private static PlayServIdentityMutationState MutationStateForRequestFailure(Exception exception)
        {
            return exception is PlayServRuntimeHttpException http &&
                   (http.IsNetworkError || http.StatusCode <= 0)
                ? PlayServIdentityMutationState.Unknown
                : PlayServIdentityMutationState.NotApplied;
        }

        private static PlayServAuthConflictParty ToConflictParty(PlayerAuthConflictPartyDto dto)
        {
            if (dto == null)
                return null;

            DateTimeOffset.TryParse(
                dto.joined,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var joinedAt);
            DateTimeOffset? lastSeenAt = DateTimeOffset.TryParse(
                dto.last_seen,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedLastSeen)
                ? (DateTimeOffset?)parsedLastSeen
                : null;
            return new PlayServAuthConflictParty(
                dto.id,
                string.Equals(dto.kind, "registered", StringComparison.OrdinalIgnoreCase)
                    ? PlayServSessionKind.Registered
                    : PlayServSessionKind.Anonymous,
                joinedAt,
                lastSeenAt);
        }

        internal static PlayServAuthError CreateAuthError(Exception exception)
        {
            if (exception is PlayServSessionRejectedException rejected)
            {
                var innerError = rejected.InnerException == null
                    ? null
                    : CreateAuthError(rejected.InnerException);
                return new PlayServAuthError(
                    innerError?.Code ?? PlayServAuthErrorCode.Unauthorized,
                    rejected.Message,
                    innerError?.HttpStatus ?? 0,
                    innerError?.BackendCode ?? string.Empty,
                    rejected,
                    innerError?.BackendTitle,
                    innerError?.BackendDetail);
            }

            if (exception is PlayServSessionPersistenceException persistence)
            {
                return CreateAuthError(
                    PlayServAuthErrorCode.PersistenceFailed,
                    persistence.Message,
                    persistence);
            }

            if (exception is PlayServFingerprintProviderException fingerprint)
            {
                return CreateAuthError(
                    PlayServAuthErrorCode.InvalidConfiguration,
                    fingerprint.Message,
                    fingerprint);
            }

            if (exception is PlayServRuntimeHttpException http)
            {
                var code = MapHttpErrorCode(http);
                return new PlayServAuthError(
                    code,
                    http.Message,
                    http.StatusCode,
                    http.BackendCode,
                    http,
                    http.ProblemTitle,
                    http.ProblemDetail);
            }

            return CreateAuthError(
                exception is InvalidOperationException
                    ? PlayServAuthErrorCode.InvalidResponse
                    : PlayServAuthErrorCode.Unknown,
                exception?.Message ?? "Unknown PlayServ authentication error.",
                exception);
        }

        internal static PlayServAuthError CreateAuthError(
            PlayServAuthErrorCode code,
            string message,
            Exception exception)
        {
            return new PlayServAuthError(
                code,
                message,
                exception is PlayServRuntimeHttpException http ? http.StatusCode : 0,
                exception is PlayServRuntimeHttpException runtimeHttp ? runtimeHttp.BackendCode : string.Empty,
                exception);
        }

        private static bool IsTerminalAuthFailure(PlayServRuntimeHttpException exception)
        {
            return exception.StatusCode == 400 ||
                   exception.StatusCode == 401 ||
                   exception.StatusCode == 403;
        }

        private static PlayServAuthErrorCode MapHttpErrorCode(PlayServRuntimeHttpException http)
        {
            if (http.IsNetworkError)
            {
                return http.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                    ? PlayServAuthErrorCode.Timeout
                    : PlayServAuthErrorCode.Network;
            }

            if (http.StatusCode == 404 &&
                string.Equals(http.BackendCode, "provider_not_linked", StringComparison.OrdinalIgnoreCase))
                return PlayServAuthErrorCode.ProviderNotLinked;
            if (http.StatusCode == 400 &&
                string.Equals(http.BackendCode, "last_provider_unlink_forbidden", StringComparison.OrdinalIgnoreCase))
                return PlayServAuthErrorCode.LastProviderUnlinkForbidden;
            if (http.StatusCode == 409 &&
                string.Equals(http.BackendCode, "merge_provider_conflict", StringComparison.OrdinalIgnoreCase))
                return PlayServAuthErrorCode.MergeProviderConflict;
            if (http.StatusCode == 401)
                return PlayServAuthErrorCode.Unauthorized;
            if (http.StatusCode == 400)
                return PlayServAuthErrorCode.InvalidCredential;
            if (http.StatusCode == 403)
            {
                return string.Equals(http.BackendCode, "device_banned", StringComparison.OrdinalIgnoreCase)
                    ? PlayServAuthErrorCode.DeviceBanned
                    : PlayServAuthErrorCode.Forbidden;
            }
            if (http.StatusCode == 409 &&
                string.Equals(http.BackendCode, "provider_already_linked", StringComparison.OrdinalIgnoreCase))
            {
                return PlayServAuthErrorCode.ProviderAlreadyLinked;
            }
            if (http.StatusCode <= 0)
                return PlayServAuthErrorCode.InvalidResponse;
            return http.StatusCode >= 500
                ? PlayServAuthErrorCode.ServerError
                : PlayServAuthErrorCode.Unknown;
        }

        private static string EmptyToNull(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private bool IsOperationCurrent(int operationGeneration) =>
            !_disposed && operationGeneration == Volatile.Read(ref _generation);

        private void ThrowIfOperationBecameStale(int operationGeneration)
        {
            if (!IsOperationCurrent(operationGeneration))
                throw new PlayServStaleSessionOperationException();
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

        private void WarnAboutDefaultRegisteredStore(PlayServSessionKind kind)
        {
#if UNITY_5_3_OR_NEWER
            if (kind != PlayServSessionKind.Registered ||
                !(_sessionStore is PlayServPlayerPrefsSessionStore) ||
                !Debug.isDebugBuild ||
                Interlocked.Exchange(ref _defaultStoreWarningLogged, 1) != 0)
            {
                return;
            }

            PlayServLog.Warning(
                PlayServLogCategory.General,
                "[PlayServ][auth] Registered refresh credentials are using the default PlayerPrefs store. Configure a platform-secure IPlayServPlayerSessionStore for production builds.");
#endif
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlayServPlayerSession));
        }
    }

    internal sealed class PlayServSessionRejectedException : Exception
    {
        public PlayServSessionRejectedException(
            PlayServSessionInfo previousSession,
            PlayServSessionLostReason reason,
            string message,
            Exception innerException = null)
            : base(message, innerException)
        {
            PreviousSession = previousSession;
            Reason = reason;
        }

        public PlayServSessionInfo PreviousSession { get; }

        public PlayServSessionLostReason Reason { get; }
    }

    internal sealed class PlayServStaleSessionOperationException : Exception
    {
        public PlayServStaleSessionOperationException()
            : base("The PlayServ player-session operation became stale before its response could be applied.")
        {
        }
    }

    internal sealed class PlayServSessionPersistenceException : Exception
    {
        public PlayServSessionPersistenceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class PlayServFingerprintProviderException : Exception
    {
        public PlayServFingerprintProviderException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
