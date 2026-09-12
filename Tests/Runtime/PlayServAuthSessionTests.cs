using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Identity;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServAuthSessionTests
    {
        [Test]
        public void Settings_DefaultAutomaticFingerprintIsEnabledAndClonePreservesOptOut()
        {
            Assert.That(new PlayServSettings().EnableAutomaticPlayerFingerprint, Is.True);

            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = false;

            Assert.That(settings.Clone().EnableAutomaticPlayerFingerprint, Is.False);
        }

        [Test]
        public void PreserveLogin_UsesCurrentPlayerAuthorizationAndRegistersSession()
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore();
            using var session = CreateSession(http, store);

            var result = session.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(http.LastLoginAuthorization, Is.EqualTo("access-anonymous-1"));
            Assert.That(http.LastLoginRequest.provider, Is.EqualTo("google"));
            Assert.That(http.LastLoginRequest.provider_token, Is.EqualTo("provider-id-token"));
            Assert.That(session.PlayerId, Is.EqualTo("player-anonymous-1"));
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.Registered));
            Assert.That(store.Session.SessionKind, Is.EqualTo(PlayServSessionKind.Registered));
        }

        [Test]
        public void RecoverLogin_OmitsAuthorizationAndReplacesPlayer()
        {
            var http = new FakeHttpClient
            {
                LoginResponse = Bundle("player-recovered", "access-recovered", "refresh-recovered")
            };
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-old",
                    "refresh-old",
                    PlayServSessionKind.Anonymous,
                    null)
            };
            using var session = CreateSession(http, store);

            var result = session.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.RecoverProviderAccount)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(http.LastLoginAuthorization, Is.Null);
            Assert.That(session.PlayerId, Is.EqualTo("player-recovered"));
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.Registered));
        }

        [Test]
        public void RecoverLogin_PersistenceFailureKeepsPreviousSession()
        {
            var http = new FakeHttpClient
            {
                LoginResponse = Bundle("player-recovered", "access-recovered", "refresh-recovered")
            };
            var previous = new PlayServPlayerSessionData(
                "player-old",
                "refresh-old",
                PlayServSessionKind.Anonymous,
                null);
            var store = new FakeSessionStore
            {
                Session = previous,
                SaveException = new InvalidOperationException("secure store unavailable")
            };
            using var session = CreateSession(http, store);

            var result = session.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.RecoverProviderAccount)
                .GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Failed));
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.PersistenceFailed));
            Assert.That(result.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Persistence));
            Assert.That(session.PlayerId, Is.EqualTo("player-old"));
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.Anonymous));
            Assert.That(store.Session, Is.SameAs(previous));
            Assert.That(http.SignedOutRefreshTokens, Does.Contain("refresh-recovered"));
        }

        [Test]
        public void ProviderConflict_ReturnsTypedConflictAndKeepsAnonymousSession()
        {
            var http = new FakeHttpClient
            {
                LoginException = new PlayServRuntimeHttpException(
                    "HTTP 409 provider_already_linked",
                    409,
                    "{\"error\":\"provider_already_linked\",\"provider\":\"google\",\"current\":{\"id\":\"plr_current\",\"kind\":\"anonymous\",\"joined\":\"2026-01-01T00:00:00Z\",\"last_seen\":\"2026-01-02T00:00:00Z\"},\"conflicting\":{\"id\":\"plr_existing\",\"kind\":\"registered\",\"joined\":\"2025-01-01T00:00:00Z\",\"last_seen\":\"2026-01-03T00:00:00Z\"}}",
                    "provider_already_linked",
                    isNetworkError: false)
            };
            var store = new FakeSessionStore();
            using var session = CreateSession(http, store);

            var result = session.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Conflict));
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.ProviderAlreadyLinked));
            Assert.That(result.Error.HttpStatus, Is.EqualTo(409));
            Assert.That(result.Error.BackendCode, Is.EqualTo("provider_already_linked"));
            Assert.That(result.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Conflict));
            Assert.That(result.UnifiedError.SourceCode, Is.EqualTo("provider_already_linked"));
            Assert.That(result.Conflict.Current.PlayerId, Is.EqualTo("plr_current"));
            Assert.That(result.Conflict.Conflicting.PlayerId, Is.EqualTo("plr_existing"));
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.Anonymous));
            Assert.That(store.Session.PlayerId, Is.EqualTo("player-anonymous-1"));
        }

        [Test]
        public void ProblemDetails_ArePreservedAsStructuredAuthError()
        {
            var error = PlayServPlayerSession.CreateAuthError(
                new PlayServRuntimeHttpException(
                    "Invalid provider credential.",
                    400,
                    "{\"title\":\"Bad request\",\"detail\":\"Invalid provider credential.\",\"code\":\"bad_request\"}",
                    "bad_request",
                    isNetworkError: false,
                    problemTitle: "Bad request",
                    problemDetail: "Invalid provider credential."));

            Assert.That(error.Code, Is.EqualTo(PlayServAuthErrorCode.InvalidCredential));
            Assert.That(error.HttpStatus, Is.EqualTo(400));
            Assert.That(error.BackendCode, Is.EqualTo("bad_request"));
            Assert.That(error.BackendTitle, Is.EqualTo("Bad request"));
            Assert.That(error.BackendDetail, Is.EqualTo("Invalid provider credential."));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Unauthorized));
            Assert.That(error.UnifiedError.RawDetails, Does.Contain("bad_request"));
        }

        [Test]
        public void Logout_RemoteFailureStillCreatesFreshAnonymousSession()
        {
            var http = new FakeHttpClient();
            http.AnonymousResponses.Clear();
            http.AnonymousResponses.Enqueue(Bundle(
                "player-anonymous-2",
                "access-anonymous-2",
                "refresh-anonymous-2"));
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-registered",
                    "refresh-registered",
                    PlayServSessionKind.Registered,
                    null)
            };
            http.SignOutException = new PlayServRuntimeHttpException(
                "network unavailable",
                0,
                string.Empty,
                string.Empty,
                isNetworkError: true);
            using var session = CreateSession(http, store);

            var result = session.LogoutAsync().GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Failed));
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.RemoteSignOutFailed));
            Assert.That(result.Session.Kind, Is.EqualTo(PlayServSessionKind.Anonymous));
            Assert.That(result.Session.PlayerId, Is.EqualTo("player-anonymous-2"));
            Assert.That(store.Session.PlayerId, Is.EqualTo("player-anonymous-2"));
        }

        [Test]
        public void Logout_WithoutPriorSessionCreatesAnonymousPlayer()
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore();
            using var session = CreateSession(http, store);

            var result = session.LogoutAsync().GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Session.Kind, Is.EqualTo(PlayServSessionKind.Anonymous));
            Assert.That(result.Session.PlayerId, Is.EqualTo("player-anonymous-1"));
            Assert.That(http.SignedOutRefreshTokens.Count, Is.Zero);
        }

        [Test]
        public void Logout_AnonymousCreationFailureLeavesNoLocalSession()
        {
            var http = new FakeHttpClient();
            http.AnonymousResponses.Clear();
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-registered",
                    "refresh-registered",
                    PlayServSessionKind.Registered,
                    null)
            };
            using var session = CreateSession(http, store);

            var result = session.LogoutAsync().GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Failed));
            Assert.That(result.Session.Kind, Is.EqualTo(PlayServSessionKind.None));
            Assert.That(store.Session, Is.Null);
        }

        [Test]
        public void TerminalRefresh_ClearsStoredSessionAndDoesNotMintAnonymousPlayer()
        {
            var http = new FakeHttpClient
            {
                RefreshException = new PlayServRuntimeHttpException(
                    "HTTP 401",
                    401,
                    string.Empty,
                    "unauthenticated",
                    isNetworkError: false)
            };
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-registered",
                    "refresh-registered",
                    PlayServSessionKind.Registered,
                    null)
            };
            using var session = CreateSession(http, store);

            Assert.Throws<PlayServSessionRejectedException>(
                () => session.RotateAccessTokenAsync().GetAwaiter().GetResult());
            Assert.That(store.Session, Is.Null);
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.None));
            Assert.That(http.AnonymousSignInCount, Is.Zero);
        }

        [Test]
        public void TransientRefreshFailure_KeepsStoredSessionForRetry()
        {
            var http = new FakeHttpClient
            {
                RefreshException = new PlayServRuntimeHttpException(
                    "network unavailable",
                    0,
                    string.Empty,
                    string.Empty,
                    isNetworkError: true)
            };
            var stored = new PlayServPlayerSessionData(
                "player-registered",
                "refresh-registered",
                PlayServSessionKind.Registered,
                null);
            var store = new FakeSessionStore { Session = stored };
            using var session = CreateSession(http, store);

            Assert.Throws<PlayServRuntimeHttpException>(
                () => session.RotateAccessTokenAsync().GetAwaiter().GetResult());
            Assert.That(store.Session, Is.SameAs(stored));
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.Registered));
            Assert.That(http.AnonymousSignInCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ConcurrentTokenRequests_ShareOneRotatedRefresh()
        {
            var response = new TaskCompletionSource<PlayerRefreshResponseDto>();
            var http = new FakeHttpClient { RefreshTask = response.Task };
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-registered",
                    "refresh-registered",
                    PlayServSessionKind.Registered,
                    null)
            };
            using var session = CreateSession(http, store);

            var first = session.GetTokenAsync();
            var second = session.GetTokenAsync();
            response.SetResult(new PlayerRefreshResponseDto
            {
                access_token = "access-refreshed",
                refresh_token = "refresh-rotated",
                expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                refresh_expires_in = 86400
            });

            var combined = Task.WhenAll(first, second);
            while (!combined.IsCompleted)
                yield return null;
            var tokens = combined.GetAwaiter().GetResult();

            Assert.That(tokens[0], Is.EqualTo("access-refreshed"));
            Assert.That(tokens[1], Is.EqualTo("access-refreshed"));
            Assert.That(http.RefreshCount, Is.EqualTo(1));
            Assert.That(store.Session.RefreshToken, Is.EqualTo("refresh-rotated"));
        }

        [UnityTest]
        public IEnumerator StaleLoginResponse_AfterDisposeDoesNotReplaceStoredSession()
        {
            var response = new TaskCompletionSource<PlayerTokenBundleDto>();
            var http = new FakeHttpClient { LoginTask = response.Task };
            var stored = new PlayServPlayerSessionData(
                "player-old",
                "refresh-old",
                PlayServSessionKind.Anonymous,
                null);
            var store = new FakeSessionStore { Session = stored };
            var session = CreateSession(http, store);

            var login = session.LoginExternalAsync(
                Proof(),
                PlayServExternalLoginMode.RecoverProviderAccount);
            session.Dispose();
            response.SetResult(Bundle("player-new", "access-new", "refresh-new"));
            while (!login.IsCompleted)
                yield return null;
            var result = login.GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Failed));
            Assert.That(store.Session, Is.SameAs(stored));
            Assert.That(http.SignedOutRefreshTokens, Does.Contain("refresh-new"));
        }

        [UnityTest]
        public IEnumerator StaleRefreshResponse_AfterDisposeIsRevokedAndNotPersisted()
        {
            var response = new TaskCompletionSource<PlayerRefreshResponseDto>();
            var http = new FakeHttpClient { RefreshTask = response.Task };
            var stored = new PlayServPlayerSessionData(
                "player-old",
                "refresh-old",
                PlayServSessionKind.Registered,
                null);
            var store = new FakeSessionStore { Session = stored };
            var session = CreateSession(http, store);

            var refresh = session.GetTokenAsync();
            session.Dispose();
            response.SetResult(new PlayerRefreshResponseDto
            {
                access_token = "access-new",
                refresh_token = "refresh-new",
                expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                refresh_expires_in = 86400
            });
            PlayServStaleSessionOperationException caught = null;
            while (!refresh.IsCompleted)
                yield return null;
            try
            {
                refresh.GetAwaiter().GetResult();
            }
            catch (PlayServStaleSessionOperationException exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(store.Session, Is.SameAs(stored));
            Assert.That(http.SignedOutRefreshTokens, Does.Contain("refresh-new"));
        }

        [Test]
        public void Coordinator_SuccessfulLoginReconnectsOnlineTransport()
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore();
            var settings = Settings();
            var disconnectCount = 0;
            var connectCount = 0;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Online,
                (_, __) => Task.FromResult(true),
                () => disconnectCount++,
                () =>
                {
                    connectCount++;
                    return Task.FromResult(true);
                });
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            var result = coordinator.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.TransportReady, Is.True);
            Assert.That(disconnectCount, Is.EqualTo(1));
            Assert.That(connectCount, Is.EqualTo(1));
        }

        [Test]
        public void Coordinator_CurrentPlayerProfileUsesOwnRuntimeRouteAndCachesEtag()
        {
            var http = new FakeHttpClient();
            http.DataResponses.Enqueue(new PlayServRuntimeDataResponse(
                200,
                RuntimePlayerJson("player-anonymous-1", "First name"),
                "\"profile-1\"",
                null));
            var store = new FakeSessionStore();
            var settings = Settings();
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Offline,
                (_, __) => Task.FromResult(true),
                () => { },
                () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            var loaded = coordinator.GetCurrentPlayerProfileAsync().GetAwaiter().GetResult();
            var cached = coordinator.GetCurrentPlayerProfileAsync().GetAwaiter().GetResult();

            Assert.That(loaded.IsSuccess, Is.True);
            Assert.That(loaded.IsFromCache, Is.False);
            Assert.That(loaded.Profile.Name, Is.EqualTo("First name"));
            Assert.That(loaded.Profile.LinkedProviders, Is.EqualTo(new[] { "google" }));
            Assert.That(loaded.Profile.JoinedDate, Is.EqualTo("2026-08-01"));
            Assert.That(loaded.ETag, Is.EqualTo("\"profile-1\""));
            Assert.That(cached.IsFromCache, Is.True);
            Assert.That(cached.Profile, Is.SameAs(loaded.Profile));
            Assert.That(coordinator.CurrentPlayerProfile, Is.SameAs(loaded.Profile));
            Assert.That(http.DataRequests, Has.Count.EqualTo(1));
            Assert.That(http.DataRequests[0].RelativePath,
                Is.EqualTo("data/players/player-anonymous-1"));
            Assert.That(http.DataRequests[0].ClientToken, Is.EqualTo("pk_public"));
            Assert.That(http.DataRequests[0].BearerToken, Is.EqualTo("access-anonymous-1"));
        }

        [Test]
        public void Coordinator_ProfileRefreshReplacesCacheAndMapsBackendFailure()
        {
            var http = new FakeHttpClient();
            http.DataResponses.Enqueue(new PlayServRuntimeDataResponse(
                200,
                RuntimePlayerJson("player-anonymous-1", "First name"),
                "\"profile-1\"",
                null));
            http.DataResponses.Enqueue(new PlayServRuntimeDataResponse(
                200,
                RuntimePlayerJson("player-anonymous-1", "Updated name"),
                "\"profile-2\"",
                null));
            http.DataExceptions.Enqueue(new PlayServRuntimeHttpException(
                "HTTP 403 player_read_forbidden",
                403,
                "{\"code\":\"player_read_forbidden\"}",
                "player_read_forbidden",
                false));
            var store = new FakeSessionStore();
            var settings = Settings();
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Offline,
                (_, __) => Task.FromResult(true),
                () => { },
                () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            coordinator.GetCurrentPlayerProfileAsync().GetAwaiter().GetResult();
            var refreshed = coordinator.RefreshCurrentPlayerProfileAsync().GetAwaiter().GetResult();
            var failed = coordinator.RefreshCurrentPlayerProfileAsync().GetAwaiter().GetResult();

            Assert.That(refreshed.Profile.Name, Is.EqualTo("Updated name"));
            Assert.That(refreshed.ETag, Is.EqualTo("\"profile-2\""));
            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(failed.Error.Code, Is.EqualTo(PlayServAuthErrorCode.Forbidden));
            Assert.That(failed.UnifiedError.SourceCode, Is.EqualTo("player_read_forbidden"));
            Assert.That(coordinator.CurrentPlayerProfile, Is.SameAs(refreshed.Profile));
        }

        [Test]
        public void Coordinator_ConflictDoesNotTouchOnlineTransport()
        {
            var http = new FakeHttpClient
            {
                LoginException = ConflictException()
            };
            var store = new FakeSessionStore();
            var settings = Settings();
            var refreshCount = 0;
            var disconnectCount = 0;
            var connectCount = 0;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Online,
                (_, __) =>
                {
                    refreshCount++;
                    return Task.FromResult(true);
                },
                () => disconnectCount++,
                () =>
                {
                    connectCount++;
                    return Task.FromResult(true);
                });
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            var result = coordinator.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Conflict));
            Assert.That(result.TransportReady, Is.True);
            Assert.That(refreshCount, Is.Zero);
            Assert.That(disconnectCount, Is.Zero);
            Assert.That(connectCount, Is.Zero);
        }

        [Test]
        public void Coordinator_CustomTokenProviderIsUnmanagedAndCannotBeOverwritten()
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore();
            var settings = Settings();
            var customProvider = new PlayServDelegateRuntimeTokenProvider(
                _ => Task.FromResult("application-managed-jwt"));
            settings.RuntimeTokenProvider = customProvider;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Offline,
                (_, __) => Task.FromResult(true),
                () => { },
                () => Task.FromResult(true));

            var before = coordinator.CurrentSession;
            var result = coordinator.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(before.Kind, Is.EqualTo(PlayServSessionKind.Unmanaged));
            Assert.That(before.PlayerId, Is.Empty);
            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Failed));
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.InvalidConfiguration));
            Assert.That(settings.RuntimeTokenProvider, Is.SameAs(customProvider));
            Assert.That(http.LastLoginRequest, Is.Null);
        }

        [Test]
        public void Coordinator_PreserveLoginTerminalRefreshRaisesSessionLostAndDoesNotReconnect()
        {
            var http = new FakeHttpClient();
            http.AnonymousResponses.Clear();
            var shortSession = Bundle(
                "player-anonymous-1",
                "access-anonymous-1",
                "refresh-anonymous-1");
            shortSession.expires_in = 1;
            http.AnonymousResponses.Enqueue(shortSession);
            var store = new FakeSessionStore();
            var settings = Settings();
            var disconnectCount = 0;
            var connectCount = 0;
            PlayServSessionLostInfo lost = null;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Online,
                (_, __) => Task.FromResult(true),
                () => disconnectCount++,
                () =>
                {
                    connectCount++;
                    return Task.FromResult(true);
                });
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
            coordinator.SessionLost += info => lost = info;
            http.RefreshException = new PlayServRuntimeHttpException(
                "HTTP 401",
                401,
                string.Empty,
                "unauthenticated",
                isNetworkError: false);

            var result = coordinator.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Failed));
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.Unauthorized));
            Assert.That(result.TransportReady, Is.False);
            Assert.That(lost, Is.Not.Null);
            Assert.That(lost.Reason, Is.EqualTo(PlayServSessionLostReason.RefreshRejected));
            Assert.That(lost.PreviousSession.PlayerId, Is.EqualTo("player-anonymous-1"));
            Assert.That(store.Session, Is.Null);
            Assert.That(disconnectCount, Is.EqualTo(1));
            Assert.That(connectCount, Is.Zero);
        }

        [TestCase("SessionRevoked", PlayServSessionLostReason.SessionRevoked)]
        [TestCase("SessionMerged", PlayServSessionLostReason.SessionMerged)]
        [TestCase("Banned", PlayServSessionLostReason.Banned)]
        [TestCase("EnvMismatch", PlayServSessionLostReason.EnvironmentMismatch)]
        [TestCase("ExpiredNoRefresh", PlayServSessionLostReason.ExpiredWithoutRefresh)]
        [TestCase("CredentialRevoked", PlayServSessionLostReason.CredentialRevoked)]
        public void Coordinator_MapsTypedCloseAndClearsSession(
            string closeReason,
            PlayServSessionLostReason expectedReason)
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore();
            var settings = Settings();
            var disconnected = false;
            var expectedThreadId = Thread.CurrentThread.ManagedThreadId;
            var eventThreadId = -1;
            var lostCount = 0;
            PlayServSessionLostInfo lost = null;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Online,
                (_, __) => Task.FromResult(true),
                () => disconnected = true,
                () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
            coordinator.SessionLost += info =>
            {
                lostCount++;
                eventThreadId = Thread.CurrentThread.ManagedThreadId;
                lost = info;
            };

            coordinator.HandleTransportClosed(new PlayServTransportCloseInfo(1008, closeReason));
            coordinator.HandleTransportClosed(new PlayServTransportCloseInfo(1008, closeReason));

            Assert.That(SpinWait.SpinUntil(() => lost != null, TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(lost.Reason, Is.EqualTo(expectedReason));
            Assert.That(lost.PreviousSession.PlayerId, Is.EqualTo("player-anonymous-1"));
            Assert.That(disconnected, Is.True);
            Assert.That(store.Session, Is.Null);
            Assert.That(eventThreadId, Is.EqualTo(expectedThreadId));
            Assert.That(lostCount, Is.EqualTo(1));
        }

        [Test]
        public void Coordinator_ExplicitLogoutSuppressesRemoteCloseAndReconnectsAnonymous()
        {
            var http = new FakeHttpClient();
            http.AnonymousResponses.Enqueue(Bundle(
                "player-anonymous-2",
                "access-anonymous-2",
                "refresh-anonymous-2"));
            var store = new FakeSessionStore();
            var settings = Settings();
            var disconnectCount = 0;
            var connectCount = 0;
            var lostCount = 0;
            PlayServPlayerAuthCoordinator coordinator = null;
            coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                store,
                () => settings,
                () => PlayServState.Online,
                (_, __) => Task.FromResult(true),
                () => disconnectCount++,
                () =>
                {
                    connectCount++;
                    return Task.FromResult(true);
                });
            using (coordinator)
            {
                coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                coordinator.SessionLost += _ => lostCount++;
                http.OnSignOut = () => coordinator.HandleTransportClosed(
                    new PlayServTransportCloseInfo(1008, "SessionRevoked"));

                var result = coordinator.LogoutAsync().GetAwaiter().GetResult();

                Assert.That(result.Session.Kind, Is.EqualTo(PlayServSessionKind.Anonymous));
                Assert.That(result.Session.PlayerId, Is.EqualTo("player-anonymous-2"));
                Assert.That(result.TransportReady, Is.True);
                Assert.That(disconnectCount, Is.EqualTo(1));
                Assert.That(connectCount, Is.EqualTo(1));
                Assert.That(lostCount, Is.Zero);
            }
        }

        [Test]
        public void ProviderDiscovery_PreservesBackendEntriesAndMarksTokenSupport()
        {
            var http = new FakeHttpClient
            {
                ProvidersResponse = new PlayerAuthProvidersProbeDto
                {
                    project = new ResolvedProjectDto { id = "prj_1", slug = "demo", env = "dev" },
                    providers = new[]
                    {
                        new PlayerAuthProviderAvailabilityDto
                        {
                            id = "google", label = "Google", enabled = true,
                            connectivity = "connected", available = true
                        },
                        new PlayerAuthProviderAvailabilityDto
                        {
                            id = "playserv-webhook", label = "Webhook", enabled = true,
                            connectivity = "connected", available = true
                        }
                    }
                }
            };
            using var session = CreateSession(http, new FakeSessionStore());

            var result = session.GetProvidersAsync().GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ProjectId, Is.EqualTo("prj_1"));
            Assert.That(result.ProjectSlug, Is.EqualTo("demo"));
            Assert.That(result.Environment, Is.EqualTo("dev"));
            Assert.That(result.Providers[0].SupportsProviderToken, Is.True);
            Assert.That(result.Providers[1].Id, Is.EqualTo("playserv-webhook"));
            Assert.That(result.Providers[1].SupportsProviderToken, Is.False);
        }

        [Test]
        public void Coordinator_ProviderDiscoveryDoesNotReplaceCustomTokenProvider()
        {
            var http = new FakeHttpClient();
            var settings = Settings();
            var custom = new PlayServDelegateRuntimeTokenProvider(_ => Task.FromResult("custom-jwt"));
            settings.RuntimeTokenProvider = custom;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                new FakeSessionStore(),
                () => settings,
                () => PlayServState.Offline,
                (_, __) => Task.FromResult(true),
                () => { },
                () => Task.FromResult(true));

            var result = coordinator.GetProvidersAsync().GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(settings.RuntimeTokenProvider, Is.SameAs(custom));
            Assert.That(coordinator.CurrentSession.Kind, Is.EqualTo(PlayServSessionKind.Unmanaged));
        }

        [Test]
        public void LinkIdentity_BootstrapsAnonymousAndAppliesRegisteredBundle()
        {
            var http = new FakeHttpClient
            {
                LinkResponse = Bundle("player-anonymous-1", Jwt("google"), "refresh-linked")
            };
            using var session = CreateSession(http, new FakeSessionStore());

            var result = session.LinkIdentityAsync(Proof()).GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.Applied));
            Assert.That(http.LastIdentityAuthorization, Is.EqualTo("access-anonymous-1"));
            Assert.That(http.LastLinkRequest.provider, Is.EqualTo("google"));
            Assert.That(result.Session.Kind, Is.EqualTo(PlayServSessionKind.Registered));
            Assert.That(result.Session.AreLinkedProvidersKnown, Is.True);
            Assert.That(result.Session.LinkedProviders, Is.EqualTo(new[] { "google" }));
        }

        [Test]
        public void LinkIdentity_ConflictKeepsCurrentSessionAndIsNotApplied()
        {
            var http = new FakeHttpClient { LinkException = ConflictException() };
            using var session = CreateSession(http, new FakeSessionStore());

            var result = session.LinkIdentityAsync(Proof()).GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Conflict));
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.NotApplied));
            Assert.That(result.Conflict.Kind, Is.EqualTo(PlayServAuthConflictKind.ProviderAlreadyLinked));
            Assert.That(result.Session.PlayerId, Is.EqualTo("player-anonymous-1"));
        }

        [Test]
        public void LinkIdentity_PlainProviderSwapConflictHasNoMergeSnapshots()
        {
            var http = new FakeHttpClient
            {
                LinkException = new PlayServRuntimeHttpException(
                    "provider already linked",
                    409,
                    "{\"code\":\"provider_already_linked\",\"provider\":\"google\"}",
                    "provider_already_linked",
                    isNetworkError: false)
            };
            using var session = CreateSession(http, new FakeSessionStore());

            var result = session.LinkIdentityAsync(Proof()).GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Conflict));
            Assert.That(result.Conflict.Kind, Is.EqualTo(PlayServAuthConflictKind.ProviderAlreadyLinked));
            Assert.That(result.Conflict.ProviderId, Is.EqualTo("google"));
            Assert.That(result.Conflict.Current, Is.Null);
            Assert.That(result.Conflict.Conflicting, Is.Null);
        }

        [Test]
        public void UnlinkIdentity_RefreshesClaimsAndKeepsSessionId()
        {
            var http = new FakeHttpClient();
            http.RefreshResponses.Enqueue(() => Refreshed(Jwt("apple", "google"), "refresh-before"));
            http.RefreshResponses.Enqueue(() => Refreshed(Jwt("apple"), "refresh-after"));
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-registered", "refresh-initial", PlayServSessionKind.Registered, null)
            };
            using var session = CreateSession(http, store);

            var result = session.UnlinkIdentityAsync("google").GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.Applied));
            Assert.That(http.LastUnlinkRequest.provider, Is.EqualTo("google"));
            Assert.That(http.RefreshCount, Is.EqualTo(2));
            Assert.That(result.Session.PlayerId, Is.EqualTo("player-registered"));
            Assert.That(result.Session.LinkedProviders, Is.EqualTo(new[] { "apple" }));
            Assert.That(store.Session.RefreshToken, Is.EqualTo("refresh-after"));
        }

        [Test]
        public void Coordinator_UnlinkUpdatesLiveAuthorizationWithoutReconnect()
        {
            var http = new FakeHttpClient();
            var settings = Settings();
            var liveAuthTokens = new List<string>();
            var disconnects = 0;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                new FakeSessionStore(),
                () => settings,
                () => PlayServState.Online,
                (token, _) =>
                {
                    liveAuthTokens.Add(token);
                    return Task.FromResult(true);
                },
                () => disconnects++,
                () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            var result = coordinator.UnlinkIdentityAsync("google").GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.TransportReady, Is.True);
            Assert.That(liveAuthTokens, Is.EqualTo(new[] { "access-refreshed" }));
            Assert.That(disconnects, Is.Zero);
        }

        [TestCase(404, "provider_not_linked", PlayServAuthErrorCode.ProviderNotLinked)]
        [TestCase(400, "last_provider_unlink_forbidden", PlayServAuthErrorCode.LastProviderUnlinkForbidden)]
        public void UnlinkIdentity_MapsBackendIdentityErrors(
            int status,
            string backendCode,
            PlayServAuthErrorCode expected)
        {
            var http = new FakeHttpClient
            {
                UnlinkException = new PlayServRuntimeHttpException(
                    backendCode, status, "{}", backendCode, isNetworkError: false)
            };
            http.RefreshResponses.Enqueue(() => Refreshed(Jwt("google"), "refresh-before"));
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-registered", "refresh-initial", PlayServSessionKind.Registered, null)
            };
            using var session = CreateSession(http, store);

            var result = session.UnlinkIdentityAsync("google").GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Failed));
            Assert.That(result.Error.Code, Is.EqualTo(expected));
            Assert.That(result.Error.UnifiedError.SourceCode, Is.EqualTo(backendCode));
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.NotApplied));
        }

        [Test]
        public void MergeIdentity_UsesConflictChoiceAndAppliesPrimarySession()
        {
            var http = new FakeHttpClient
            {
                MergeResponse = Bundle("player-existing", Jwt("google"), "refresh-merged")
            };
            http.RefreshResponses.Enqueue(() => Refreshed(Jwt("apple"), "refresh-current"));
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-current", "refresh-initial", PlayServSessionKind.Registered, null)
            };
            using var session = CreateSession(http, store);

            var result = session.MergeIdentityAsync(
                    MergeConflict(),
                    PlayServMergeChoice.UseConflictingPlayer,
                    Proof())
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.Applied));
            Assert.That(http.LastMergeRequest.primary_plr_id, Is.EqualTo("player-existing"));
            Assert.That(http.LastMergeRequest.absorbed_plr_id, Is.EqualTo("player-current"));
            Assert.That(result.Session.PlayerId, Is.EqualTo("player-existing"));
        }

        [Test]
        public void MergeIdentity_ProviderOverlapReturnsTypedConflict()
        {
            var http = new FakeHttpClient
            {
                MergeException = new PlayServRuntimeHttpException(
                    "merge conflict",
                    409,
                    "{\"code\":\"merge_provider_conflict\",\"providers\":[\"google\",\"apple\"]}",
                    "merge_provider_conflict",
                    isNetworkError: false)
            };
            http.RefreshResponses.Enqueue(() => Refreshed(Jwt("apple"), "refresh-current"));
            using var session = CreateSession(http, new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-current", "refresh-initial", PlayServSessionKind.Registered, null)
            });

            var result = session.MergeIdentityAsync(
                    MergeConflict(), PlayServMergeChoice.KeepCurrentPlayer, Proof())
                .GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(PlayServAuthOperationStatus.Conflict));
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.MergeProviderConflict));
            Assert.That(result.Conflict.Kind, Is.EqualTo(PlayServAuthConflictKind.MergeProviderConflict));
            Assert.That(result.Conflict.ConflictingProviders, Is.EqualTo(new[] { "apple", "google" }));
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.NotApplied));
        }

        [Test]
        public void MergeIdentity_ForbiddenWithRejectedRefreshInfersCommittedBan()
        {
            var http = new FakeHttpClient();
            http.RefreshResponses.Enqueue(() => Refreshed(Jwt("apple"), "refresh-current"));
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-current", "refresh-initial", PlayServSessionKind.Registered, null)
            };
            using var session = CreateSession(http, store);
            session.GetTokenAsync().GetAwaiter().GetResult();
            http.MergeException = new PlayServRuntimeHttpException(
                "forbidden", 403, "{}", "forbidden", isNetworkError: false);
            http.RefreshException = new PlayServRuntimeHttpException(
                "unauthorized", 401, "{}", "unauthenticated", isNetworkError: false);

            var result = session.MergeIdentityAsync(
                    MergeConflict(), PlayServMergeChoice.KeepCurrentPlayer, Proof())
                .GetAwaiter().GetResult();

            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.DeviceBanned));
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.Applied));
            Assert.That(result.Session.Kind, Is.EqualTo(PlayServSessionKind.None));
            Assert.That(store.Session, Is.Null);
        }

        [Test]
        public void MergeIdentity_ForbiddenWithTransientRefreshReturnsUnknownOutcome()
        {
            var http = new FakeHttpClient();
            http.RefreshResponses.Enqueue(() => Refreshed(Jwt("apple"), "refresh-current"));
            using var session = CreateSession(http, new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData(
                    "player-current", "refresh-initial", PlayServSessionKind.Registered, null)
            });
            session.GetTokenAsync().GetAwaiter().GetResult();
            http.MergeException = new PlayServRuntimeHttpException(
                "forbidden", 403, "{}", "forbidden", isNetworkError: false);
            http.RefreshException = new PlayServRuntimeHttpException(
                "network unavailable", 0, string.Empty, string.Empty, isNetworkError: true);

            var result = session.MergeIdentityAsync(
                    MergeConflict(), PlayServMergeChoice.KeepCurrentPlayer, Proof())
                .GetAwaiter().GetResult();

            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.MergeOutcomeUnknown));
            Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.Unknown));
            Assert.That(result.Session.PlayerId, Is.EqualTo("player-current"));
        }

        [Test]
        public void Coordinator_SuccessfulMergeAbsorbsExpectedSessionMergedClose()
        {
            var http = new FakeHttpClient
            {
                MergeResponse = Bundle("player-anonymous-1", Jwt("google"), "refresh-merged")
            };
            var settings = Settings();
            var disconnects = 0;
            var connects = 0;
            var lost = 0;
            PlayServPlayerAuthCoordinator coordinator = null;
            coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                new FakeSessionStore(),
                () => settings,
                () => PlayServState.Online,
                (_, __) => Task.FromResult(true),
                () => disconnects++,
                () => { connects++; return Task.FromResult(true); });
            using (coordinator)
            {
                coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                coordinator.SessionLost += _ => lost++;
                http.OnMerge = () => coordinator.HandleTransportClosed(
                    new PlayServTransportCloseInfo(1008, "SessionMerged"));

                var result = coordinator.MergeIdentityAsync(
                        MergeConflict("player-anonymous-1"),
                        PlayServMergeChoice.KeepCurrentPlayer,
                        Proof())
                    .GetAwaiter().GetResult();

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.TransportReady, Is.True);
                Assert.That(lost, Is.Zero);
                Assert.That(disconnects, Is.EqualTo(1));
                Assert.That(connects, Is.EqualTo(1));
            }
        }

        [Test]
        public void Coordinator_SuccessfulMergeAbsorbsLateSessionMergedClose()
        {
            var http = new FakeHttpClient
            {
                MergeResponse = Bundle("player-anonymous-1", Jwt("google"), "refresh-merged")
            };
            var settings = Settings();
            var lost = 0;
            using var coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                new FakeSessionStore(),
                () => settings,
                () => PlayServState.Online,
                (_, __) => Task.FromResult(true),
                () => { },
                () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
            coordinator.SessionLost += _ => lost++;

            var result = coordinator.MergeIdentityAsync(
                    MergeConflict("player-anonymous-1"),
                    PlayServMergeChoice.KeepCurrentPlayer,
                    Proof())
                .GetAwaiter().GetResult();
            coordinator.HandleTransportClosed(new PlayServTransportCloseInfo(1008, "SessionMerged"));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.TransportReady, Is.True);
            Assert.That(lost, Is.Zero);
            Assert.That(coordinator.CurrentSession.PlayerId, Is.EqualTo("player-anonymous-1"));
        }

        [Test]
        public void Coordinator_CommittedBannedMergeRaisesSessionLostOnlyOnce()
        {
            var http = new FakeHttpClient
            {
                MergeException = new PlayServRuntimeHttpException(
                    "forbidden", 403, "{}", "forbidden", isNetworkError: false),
                RefreshException = new PlayServRuntimeHttpException(
                    "unauthorized", 401, "{}", "unauthenticated", isNetworkError: false)
            };
            var settings = Settings();
            var lostCount = 0;
            PlayServSessionLostInfo lost = null;
            PlayServPlayerAuthCoordinator coordinator = null;
            coordinator = new PlayServPlayerAuthCoordinator(
                _ => http,
                new FakeSessionStore(),
                () => settings,
                () => PlayServState.Online,
                (_, __) => Task.FromResult(true),
                () => { },
                () => Task.FromResult(true));
            using (coordinator)
            {
                coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                coordinator.SessionLost += info => { lostCount++; lost = info; };
                http.OnMerge = () => coordinator.HandleTransportClosed(
                    new PlayServTransportCloseInfo(1008, "Banned"));

                var result = coordinator.MergeIdentityAsync(
                        MergeConflict("player-anonymous-1"),
                        PlayServMergeChoice.KeepCurrentPlayer,
                        Proof())
                    .GetAwaiter().GetResult();

                Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.DeviceBanned));
                Assert.That(result.IdentityMutationState, Is.EqualTo(PlayServIdentityMutationState.Applied));
                Assert.That(lostCount, Is.EqualTo(1));
                Assert.That(lost.Reason, Is.EqualTo(PlayServSessionLostReason.Banned));
                Assert.That(coordinator.CurrentSession.Kind, Is.EqualTo(PlayServSessionKind.None));
            }
        }

        [Test]
        public void CustomFingerprint_IsSentOnAnonymousAndBothProviderLoginModes()
        {
            var fingerprintProvider = new FakeFingerprintProvider();
            var settings = Settings();
            settings.PlayerFingerprintProvider = fingerprintProvider;
            var http = new FakeHttpClient();
            using var session = CreateSession(http, new FakeSessionStore(), settings);

            session.GetTokenAsync().GetAwaiter().GetResult();
            session.LoginExternalAsync(Proof(), PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(fingerprintProvider.CallCount, Is.EqualTo(2));
            Assert.That(http.LastAnonymousFingerprint.stable["platform"], Is.EqualTo("windows"));
            Assert.That(http.LastLoginRequest.fingerprint.stable["platform"], Is.EqualTo("windows"));

            var recoveryHttp = new FakeHttpClient();
            var recoveryProvider = new FakeFingerprintProvider();
            var recoverySettings = Settings();
            recoverySettings.PlayerFingerprintProvider = recoveryProvider;
            using var recovery = CreateSession(recoveryHttp, new FakeSessionStore(), recoverySettings);
            recovery.LoginExternalAsync(Proof(), PlayServExternalLoginMode.RecoverProviderAccount)
                .GetAwaiter().GetResult();
            Assert.That(recoveryProvider.CallCount, Is.EqualTo(1));
            Assert.That(recoveryHttp.LastLoginRequest.fingerprint.soft["app_version"], Is.EqualTo("1.0.0"));
        }

        [Test]
        public void AutomaticFingerprint_IsReusedAcrossAnonymousBootstrapAndPreserveLogin()
        {
            var deviceInfo = new FakeUnityDeviceInfo(
                "android",
                "com.playserv.test",
                "raw-device-identifier");
            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = true;
            var http = new FakeHttpClient();
            using var session = CreateSession(http, new FakeSessionStore(), settings, deviceInfo);

            var result = session.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            var expectedHash = PlayServAutomaticPlayerFingerprintProvider.ComputeDeviceIdHash(
                "android",
                "com.playserv.test",
                "raw-device-identifier");
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(deviceInfo.DeviceIdentifierReadCount, Is.EqualTo(1));
            Assert.That(http.LastAnonymousFingerprint.stable["device_id_hash"], Is.EqualTo(expectedHash));
            Assert.That(http.LastLoginRequest.fingerprint.stable["device_id_hash"], Is.EqualTo(expectedHash));
            Assert.That(expectedHash, Does.Not.Contain("raw-device-identifier"));
        }

        [Test]
        public void AutomaticFingerprint_IsSentOnProviderRecoveryAndMapsDeviceBanWithoutRetry()
        {
            var deviceInfo = new FakeUnityDeviceInfo(
                "ios",
                "com.playserv.test",
                "ios-vendor-identifier");
            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = true;
            var http = new FakeHttpClient
            {
                LoginException = new PlayServRuntimeHttpException(
                    "device banned",
                    403,
                    "{\"code\":\"device_banned\"}",
                    "device_banned",
                    isNetworkError: false)
            };
            var store = new FakeSessionStore();
            using var session = CreateSession(http, store, settings, deviceInfo);

            var result = session.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.RecoverProviderAccount)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.DeviceBanned));
            Assert.That(http.LastLoginRequest.fingerprint.stable["platform"], Is.EqualTo("ios"));
            Assert.That(http.AnonymousSignInCount, Is.Zero);
            Assert.That(store.Session, Is.Null);
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.None));
        }

        [Test]
        public void AutomaticFingerprint_DeviceBanDuringAnonymousBootstrapStopsPreserveLogin()
        {
            var deviceInfo = new FakeUnityDeviceInfo(
                "android",
                "com.playserv.test",
                "banned-device-identifier");
            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = true;
            var http = new FakeHttpClient
            {
                AnonymousException = new PlayServRuntimeHttpException(
                    "device banned",
                    403,
                    "{\"code\":\"device_banned\"}",
                    "device_banned",
                    isNetworkError: false)
            };
            var store = new FakeSessionStore();
            using var session = CreateSession(http, store, settings, deviceInfo);

            var result = session.LoginExternalAsync(
                    Proof(),
                    PlayServExternalLoginMode.PreserveCurrentPlayer)
                .GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.DeviceBanned));
            Assert.That(http.LastAnonymousFingerprint.stable["platform"], Is.EqualTo("android"));
            Assert.That(http.AnonymousSignInCount, Is.EqualTo(1));
            Assert.That(http.ExternalLoginCount, Is.Zero);
            Assert.That(store.Session, Is.Null);
            Assert.That(session.SessionKind, Is.EqualTo(PlayServSessionKind.None));
        }

        [Test]
        public void AutomaticFingerprint_DoesNotRestoreAPlayerFromTheDeviceHash()
        {
            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = true;
            var firstHttp = new FakeHttpClient();
            var secondHttp = new FakeHttpClient();
            secondHttp.AnonymousResponses.Clear();
            secondHttp.AnonymousResponses.Enqueue(Bundle(
                "player-anonymous-2",
                "access-anonymous-2",
                "refresh-anonymous-2"));
            using var first = CreateSession(
                firstHttp,
                new FakeSessionStore(),
                settings,
                new FakeUnityDeviceInfo("android", "com.playserv.test", "same-device"));
            using var second = CreateSession(
                secondHttp,
                new FakeSessionStore(),
                settings,
                new FakeUnityDeviceInfo("android", "com.playserv.test", "same-device"));

            first.GetTokenAsync().GetAwaiter().GetResult();
            second.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(first.PlayerId, Is.EqualTo("player-anonymous-1"));
            Assert.That(second.PlayerId, Is.EqualTo("player-anonymous-2"));
            Assert.That(
                firstHttp.LastAnonymousFingerprint.stable["device_id_hash"],
                Is.EqualTo(secondHttp.LastAnonymousFingerprint.stable["device_id_hash"]));
            Assert.That(firstHttp.AnonymousSignInCount, Is.EqualTo(1));
            Assert.That(secondHttp.AnonymousSignInCount, Is.EqualTo(1));
        }

        [Test]
        public void Fingerprint_IsNotCollectedForRefreshOrIdentityMutations()
        {
            var fingerprintProvider = new FakeFingerprintProvider();
            var settings = Settings();
            settings.PlayerFingerprintProvider = fingerprintProvider;
            var http = new FakeHttpClient
            {
                LinkResponse = Bundle(
                    "player-anonymous-1",
                    Jwt("google"),
                    "refresh-linked"),
                MergeResponse = Bundle(
                    "player-anonymous-1",
                    Jwt("google"),
                    "refresh-merged")
            };
            using var session = CreateSession(http, new FakeSessionStore(), settings);

            session.GetTokenAsync().GetAwaiter().GetResult();
            Assert.That(fingerprintProvider.CallCount, Is.EqualTo(1));

            session.RotateAccessTokenAsync().GetAwaiter().GetResult();
            session.LinkIdentityAsync(Proof()).GetAwaiter().GetResult();
            session.UnlinkIdentityAsync("google").GetAwaiter().GetResult();
            session.MergeIdentityAsync(
                    MergeConflict("player-anonymous-1"),
                    PlayServMergeChoice.KeepCurrentPlayer,
                    Proof())
                .GetAwaiter().GetResult();

            Assert.That(fingerprintProvider.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void CustomFingerprint_TakesPrecedenceOverAutomaticCollection()
        {
            var custom = new FakeFingerprintProvider();
            var deviceInfo = new FakeUnityDeviceInfo(
                "android",
                "com.playserv.test",
                "raw-device-identifier");
            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = true;
            settings.PlayerFingerprintProvider = custom;
            var http = new FakeHttpClient();
            using var session = CreateSession(http, new FakeSessionStore(), settings, deviceInfo);

            session.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(custom.CallCount, Is.EqualTo(1));
            Assert.That(deviceInfo.DeviceIdentifierReadCount, Is.Zero);
            Assert.That(http.LastAnonymousFingerprint.stable["platform"], Is.EqualTo("windows"));
        }

        [Test]
        public void CustomFingerprint_NullResultDoesNotFallBackToAutomaticCollection()
        {
            var custom = new NullFingerprintProvider();
            var deviceInfo = new FakeUnityDeviceInfo(
                "android",
                "com.playserv.test",
                "must-not-be-read");
            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = true;
            settings.PlayerFingerprintProvider = custom;
            var http = new FakeHttpClient();
            using var session = CreateSession(http, new FakeSessionStore(), settings, deviceInfo);

            session.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(custom.CallCount, Is.EqualTo(1));
            Assert.That(deviceInfo.DeviceIdentifierReadCount, Is.Zero);
            Assert.That(http.LastAnonymousFingerprint, Is.Null);
        }

        [Test]
        public void AutomaticFingerprint_HashIsDeterministicAndApplicationScoped()
        {
            var first = PlayServAutomaticPlayerFingerprintProvider.ComputeDeviceIdHash(
                "android",
                "com.playserv.first",
                "raw-device-identifier");
            var repeated = PlayServAutomaticPlayerFingerprintProvider.ComputeDeviceIdHash(
                "android",
                "com.playserv.first",
                "raw-device-identifier");
            var anotherApplication = PlayServAutomaticPlayerFingerprintProvider.ComputeDeviceIdHash(
                "android",
                "com.playserv.second",
                "raw-device-identifier");
            var anotherPlatform = PlayServAutomaticPlayerFingerprintProvider.ComputeDeviceIdHash(
                "ios",
                "com.playserv.first",
                "raw-device-identifier");

            Assert.That(first, Is.EqualTo(repeated));
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(first, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(anotherApplication, Is.Not.EqualTo(first));
            Assert.That(anotherPlatform, Is.Not.EqualTo(first));
        }

        [Test]
        public void AutomaticFingerprint_UnsupportedPlatformWarnsOnceWithoutReadingDeviceIdentifier()
        {
            PlayServAutomaticPlayerFingerprintProvider.ResetWarningForTests();
            var deviceInfo = new FakeUnityDeviceInfo(
                platform: null,
                applicationIdentifier: "com.playserv.test",
                deviceUniqueIdentifier: "must-not-be-read",
                developmentWarningsEnabled: true);
            var provider = new PlayServAutomaticPlayerFingerprintProvider(deviceInfo);

            var first = provider.GetFingerprintAsync().GetAwaiter().GetResult();
            var second = provider.GetFingerprintAsync().GetAwaiter().GetResult();

            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(deviceInfo.WarningCount, Is.EqualTo(1));
            Assert.That(deviceInfo.DeviceIdentifierReadCount, Is.Zero);
        }

        [Test]
        public void AutomaticFingerprint_UnsupportedDeviceIdentifierIsOmittedAndWarnedOnce()
        {
            PlayServAutomaticPlayerFingerprintProvider.ResetWarningForTests();
            var deviceInfo = new FakeUnityDeviceInfo(
                "android",
                "com.playserv.test",
                UnityEngine.SystemInfo.unsupportedIdentifier,
                developmentWarningsEnabled: true);
            var provider = new PlayServAutomaticPlayerFingerprintProvider(deviceInfo);

            var fingerprint = provider.GetFingerprintAsync().GetAwaiter().GetResult();

            Assert.That(fingerprint, Is.Null);
            Assert.That(deviceInfo.DeviceIdentifierReadCount, Is.EqualTo(1));
            Assert.That(deviceInfo.WarningCount, Is.EqualTo(1));
        }

        [Test]
        public void AutomaticFingerprint_CanBeDisabledWithoutReadingDeviceIdentifier()
        {
            var deviceInfo = new FakeUnityDeviceInfo(
                "android",
                "com.playserv.test",
                "must-not-be-read");
            var settings = Settings();
            settings.EnableAutomaticPlayerFingerprint = false;
            var http = new FakeHttpClient();
            using var session = CreateSession(http, new FakeSessionStore(), settings, deviceInfo);

            session.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(deviceInfo.DeviceIdentifierReadCount, Is.Zero);
            Assert.That(http.LastAnonymousFingerprint, Is.Null);
        }

        [Test]
        public void AutomaticFingerprint_HonorsCancellationBeforeReadingUnityDeviceInfo()
        {
            var deviceInfo = new FakeUnityDeviceInfo(
                "android",
                "com.playserv.test",
                "must-not-be-read");
            var provider = new PlayServAutomaticPlayerFingerprintProvider(deviceInfo);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                provider.GetFingerprintAsync(cancellation.Token).GetAwaiter().GetResult());
            Assert.That(deviceInfo.DeviceIdentifierReadCount, Is.Zero);
        }

        [Test]
        public void Fingerprint_RejectsUnknownOrForbiddenSignalsBeforeNetworkUse()
        {
            Assert.Throws<ArgumentException>(() => new PlayServPlayerFingerprint(
                new Dictionary<string, object>
                {
                    ["platform"] = "windows",
                    ["advertising_id"] = "forbidden"
                }));
            Assert.Throws<ArgumentException>(() => new PlayServPlayerFingerprint(
                new Dictionary<string, object> { ["platform"] = "unknown" }));
            Assert.Throws<ArgumentException>(() => new PlayServPlayerFingerprint(
                new Dictionary<string, object>
                {
                    ["platform"] = "windows",
                    ["device_model"] = "desktop-model"
                }));
            Assert.Throws<ArgumentException>(() => new PlayServPlayerFingerprint(
                new Dictionary<string, object>
                {
                    ["platform"] = "android",
                    ["device_id_hash"] = new string('A', 64)
                }));
            Assert.Throws<ArgumentException>(() => new PlayServPlayerFingerprint(
                new Dictionary<string, object>
                {
                    ["platform"] = "web",
                    ["device_id_hash"] = new string('a', 64)
                }));
        }

        [Test]
        public void ProviderProofFactoriesUseBackendProviderAndModeContracts()
        {
            var facebook = PlayServExternalIdentityProof.FromFacebookLimitedLoginToken("token", "nonce");
            var epic = PlayServExternalIdentityProof.FromEpicExternalAuthToken(
                "exchange-code",
                launcherExchangeCode: true);
            var steam = PlayServExternalIdentityProof.FromSteamTicket("ticket");
            var custom = PlayServExternalIdentityProof.FromPlayServToken("jwt");

            Assert.That(facebook.ProviderId, Is.EqualTo("facebook"));
            Assert.That(facebook.Nonce, Is.EqualTo("nonce"));
            Assert.That(epic.ProviderId, Is.EqualTo("epic"));
            Assert.That(epic.Mode, Is.EqualTo("launcher_exchange_code"));
            Assert.That(steam.ProviderId, Is.EqualTo("steam"));
            Assert.That(custom.ProviderId, Is.EqualTo("playserv-token"));
        }

        private static PlayServPlayerSession CreateSession(
            IPlayServRuntimeHttpClient http,
            IPlayServPlayerSessionStore store,
            PlayServSettings settings = null,
            IPlayServUnityDeviceInfo unityDeviceInfo = null) =>
            new PlayServPlayerSession(
                settings ?? Settings(),
                http,
                store,
                SynchronizationContext.Current,
                unityDeviceInfo);

        private static PlayServSettings Settings() => new PlayServSettings
        {
            ClientToken = "pk_public",
            DeploymentGameId = "test-game",
            GameVersion = "1.0.0",
            BackendServerAddress = "wss://example.test/ws",
            EnableAutomaticPlayerFingerprint = false
        };

        private static PlayServExternalIdentityProof Proof() =>
            PlayServExternalIdentityProof.FromProviderToken("google", "provider-id-token");

        private static string RuntimePlayerJson(string playerId, string name) =>
            "{\"id\":\"" + playerId + "\",\"kind\":\"anonymous\",\"name\":\"" + name +
            "\",\"email\":null,\"status\":\"active\",\"sso\":[\"google\"]," +
            "\"country\":\"UA\",\"last_seen\":\"2026-08-28T10:00:00Z\"," +
            "\"joined\":\"2026-08-01\",\"created_at\":\"2026-08-01T00:00:00Z\"," +
            "\"updated_at\":\"2026-08-28T10:00:00Z\"}";

        private static PlayServRuntimeHttpException ConflictException() =>
            new PlayServRuntimeHttpException(
                "HTTP 409 provider_already_linked",
                409,
                "{\"error\":\"provider_already_linked\",\"provider\":\"google\",\"current\":{\"id\":\"plr_current\",\"kind\":\"anonymous\",\"joined\":\"2026-01-01T00:00:00Z\",\"last_seen\":\"2026-01-02T00:00:00Z\"},\"conflicting\":{\"id\":\"plr_existing\",\"kind\":\"registered\",\"joined\":\"2025-01-01T00:00:00Z\",\"last_seen\":\"2026-01-03T00:00:00Z\"}}",
                "provider_already_linked",
                isNetworkError: false);

        private static PlayServAuthConflict MergeConflict(string currentPlayerId = "player-current") =>
            new PlayServAuthConflict(
                "google",
                new PlayServAuthConflictParty(
                    currentPlayerId,
                    PlayServSessionKind.Registered,
                    DateTimeOffset.UtcNow.AddDays(-2),
                    DateTimeOffset.UtcNow),
                new PlayServAuthConflictParty(
                    "player-existing",
                    PlayServSessionKind.Registered,
                    DateTimeOffset.UtcNow.AddDays(-10),
                    DateTimeOffset.UtcNow),
                PlayServAuthConflictKind.ProviderAlreadyLinked);

        private static PlayerRefreshResponseDto Refreshed(string accessToken, string refreshToken) =>
            new PlayerRefreshResponseDto
            {
                access_token = accessToken,
                refresh_token = refreshToken,
                expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                refresh_expires_in = 86400
            };

        private static string Jwt(params string[] providers)
        {
            var providerJson = string.Join(",", Array.ConvertAll(
                providers ?? Array.Empty<string>(),
                provider => $"\"{provider}\""));
            return $"{Base64Url("{\"alg\":\"none\"}")}.{Base64Url($"{{\"providers\":[{providerJson}]}}")}.signature";
        }

        private static string Base64Url(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static PlayerTokenBundleDto Bundle(string playerId, string accessToken, string refreshToken) =>
            new PlayerTokenBundleDto
            {
                player_id = playerId,
                access_token = accessToken,
                refresh_token = refreshToken,
                expires_in = 3600,
                refresh_expires_in = 86400,
                issued_at = DateTimeOffset.UtcNow.ToString("O")
            };

        private sealed class FakeSessionStore : IPlayServPlayerSessionStore
        {
            public PlayServPlayerSessionData Session { get; set; }

            public Exception SaveException { get; set; }

            public Task<PlayServPlayerSessionData> LoadAsync(
                string scopeKey,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(Session);

            public Task SaveAsync(
                string scopeKey,
                PlayServPlayerSessionData session,
                CancellationToken cancellationToken = default)
            {
                if (SaveException != null)
                    throw SaveException;

                Session = session;
                return Task.CompletedTask;
            }

            public Task ClearAsync(
                string scopeKey,
                CancellationToken cancellationToken = default)
            {
                Session = null;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeFingerprintProvider : IPlayServPlayerFingerprintProvider
        {
            public int CallCount { get; private set; }

            public Task<PlayServPlayerFingerprint> GetFingerprintAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                return Task.FromResult(new PlayServPlayerFingerprint(
                    new Dictionary<string, object> { ["platform"] = "windows" },
                    new Dictionary<string, object> { ["app_version"] = "1.0.0" }));
            }
        }

        private sealed class NullFingerprintProvider : IPlayServPlayerFingerprintProvider
        {
            public int CallCount { get; private set; }

            public Task<PlayServPlayerFingerprint> GetFingerprintAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                return Task.FromResult<PlayServPlayerFingerprint>(null);
            }
        }

        private sealed class FakeUnityDeviceInfo : IPlayServUnityDeviceInfo
        {
            private readonly string _deviceUniqueIdentifier;

            public FakeUnityDeviceInfo(
                string platform,
                string applicationIdentifier,
                string deviceUniqueIdentifier,
                bool developmentWarningsEnabled = false)
            {
                Platform = platform;
                ApplicationIdentifier = applicationIdentifier;
                _deviceUniqueIdentifier = deviceUniqueIdentifier;
                DevelopmentWarningsEnabled = developmentWarningsEnabled;
            }

            public string Platform { get; }

            public string ApplicationIdentifier { get; }

            public string DeviceUniqueIdentifier
            {
                get
                {
                    DeviceIdentifierReadCount++;
                    return _deviceUniqueIdentifier;
                }
            }

            public bool DevelopmentWarningsEnabled { get; }

            public int DeviceIdentifierReadCount { get; private set; }

            public int WarningCount { get; private set; }

            public void LogWarning(string message) => WarningCount++;
        }

        private sealed class FakeHttpClient :
            IPlayServRuntimeHttpClient,
            IPlayServPlayerIdentityHttpClient
        {
            public FakeHttpClient()
            {
                AnonymousResponses.Enqueue(Bundle(
                    "player-anonymous-1",
                    "access-anonymous-1",
                    "refresh-anonymous-1"));
                LoginResponse = Bundle(
                    "player-anonymous-1",
                    "access-registered",
                    "refresh-registered");
            }

            public Queue<PlayerTokenBundleDto> AnonymousResponses { get; } =
                new Queue<PlayerTokenBundleDto>();

            public PlayerTokenBundleDto LoginResponse { get; set; }

            public PlayerTokenBundleDto LinkResponse { get; set; }

            public PlayerTokenBundleDto MergeResponse { get; set; }

            public PlayerAuthProvidersProbeDto ProvidersResponse { get; set; }

            public Exception LoginException { get; set; }

            public Exception AnonymousException { get; set; }

            public Exception LinkException { get; set; }

            public Exception UnlinkException { get; set; }

            public Exception MergeException { get; set; }

            public Exception RefreshException { get; set; }

            public Task<PlayerRefreshResponseDto> RefreshTask { get; set; }

            public Exception SignOutException { get; set; }

            public Task<PlayerTokenBundleDto> LoginTask { get; set; }

            public Action OnSignOut { get; set; }

            public Action OnMerge { get; set; }

            public PlayerExternalLoginRequestDto LastLoginRequest { get; private set; }

            public PlayerLinkRequestDto LastLinkRequest { get; private set; }

            public PlayerUnlinkRequestDto LastUnlinkRequest { get; private set; }

            public PlayerMergeRequestDto LastMergeRequest { get; private set; }

            public PlayerFingerprintDto LastAnonymousFingerprint { get; private set; }

            public string LastLoginAuthorization { get; private set; }

            public string LastIdentityAuthorization { get; private set; }

            public int AnonymousSignInCount { get; private set; }

            public int ExternalLoginCount { get; private set; }

            public int RefreshCount { get; private set; }

            public List<string> SignedOutRefreshTokens { get; } = new List<string>();

            public Queue<Func<PlayerRefreshResponseDto>> RefreshResponses { get; } =
                new Queue<Func<PlayerRefreshResponseDto>>();

            public Queue<PlayServRuntimeDataResponse> DataResponses { get; } =
                new Queue<PlayServRuntimeDataResponse>();

            public Queue<Exception> DataExceptions { get; } = new Queue<Exception>();

            public List<PlayServRuntimeDataRequest> DataRequests { get; } =
                new List<PlayServRuntimeDataRequest>();

            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) =>
                Task.FromResult("1.0.0");

            public Task<PlayerTokenBundleDto> SignInAnonAsync(
                string clientToken,
                CancellationToken ct = default) =>
                SignInAnonAsync(clientToken, null, ct);

            public Task<PlayerTokenBundleDto> SignInAnonAsync(
                string clientToken,
                PlayerFingerprintDto fingerprint,
                CancellationToken ct = default)
            {
                AnonymousSignInCount++;
                LastAnonymousFingerprint = fingerprint;
                if (AnonymousException != null)
                    throw AnonymousException;
                return Task.FromResult(AnonymousResponses.Dequeue());
            }

            public Task<PlayerRefreshResponseDto> RefreshAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default)
            {
                RefreshCount++;
                if (RefreshResponses.Count > 0)
                    return Task.FromResult(RefreshResponses.Dequeue()());
                if (RefreshException != null)
                    throw RefreshException;

                if (RefreshTask != null)
                    return RefreshTask;

                return Task.FromResult(new PlayerRefreshResponseDto
                {
                    access_token = "access-refreshed",
                    refresh_token = "refresh-rotated",
                    expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                    refresh_expires_in = 86400
                });
            }

            public Task<PlayerTokenBundleDto> LoginExternalAsync(
                string clientToken,
                PlayerExternalLoginRequestDto request,
                string playerAccessToken = null,
                CancellationToken ct = default)
            {
                ExternalLoginCount++;
                LastLoginRequest = request;
                LastLoginAuthorization = playerAccessToken;
                if (LoginException != null)
                    throw LoginException;
                return LoginTask ?? Task.FromResult(LoginResponse);
            }

            public Task SignOutAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default)
            {
                SignedOutRefreshTokens.Add(refreshToken);
                OnSignOut?.Invoke();
                if (SignOutException != null)
                    throw SignOutException;
                return Task.CompletedTask;
            }

            public Task<PlayerAuthProvidersProbeDto> GetAuthProvidersAsync(
                string clientToken,
                CancellationToken ct = default) =>
                Task.FromResult(ProvidersResponse ?? new PlayerAuthProvidersProbeDto
                {
                    project = new ResolvedProjectDto { id = "prj_test", slug = "test", env = "dev" },
                    providers = Array.Empty<PlayerAuthProviderAvailabilityDto>()
                });

            public Task<PlayerTokenBundleDto> LinkIdentityAsync(
                string clientToken,
                PlayerLinkRequestDto request,
                string playerAccessToken,
                CancellationToken ct = default)
            {
                LastLinkRequest = request;
                LastIdentityAuthorization = playerAccessToken;
                if (LinkException != null)
                    throw LinkException;
                return Task.FromResult(LinkResponse ?? LoginResponse);
            }

            public Task UnlinkIdentityAsync(
                string clientToken,
                PlayerUnlinkRequestDto request,
                string playerAccessToken,
                CancellationToken ct = default)
            {
                LastUnlinkRequest = request;
                LastIdentityAuthorization = playerAccessToken;
                if (UnlinkException != null)
                    throw UnlinkException;
                return Task.CompletedTask;
            }

            public Task<PlayerTokenBundleDto> MergeIdentityAsync(
                string clientToken,
                PlayerMergeRequestDto request,
                string playerAccessToken,
                CancellationToken ct = default)
            {
                LastMergeRequest = request;
                LastIdentityAuthorization = playerAccessToken;
                OnMerge?.Invoke();
                if (MergeException != null)
                    throw MergeException;
                return Task.FromResult(MergeResponse ?? LoginResponse);
            }

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                DataRequests.Add(request);
                if (DataResponses.Count > 0)
                    return Task.FromResult(DataResponses.Dequeue());
                if (DataExceptions.Count > 0)
                    throw DataExceptions.Dequeue();
                throw new NotSupportedException();
            }
        }
    }
}
