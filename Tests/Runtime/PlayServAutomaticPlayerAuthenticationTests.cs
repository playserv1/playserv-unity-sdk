using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServAutomaticPlayerAuthenticationTests
    {
        [Test]
        public void EmptyStore_CreatesAndPersistsAnonymousSession()
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore();
            using var session = CreateSession(http, store);

            var token = session.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(token, Is.EqualTo("access-created"));
            Assert.That(session.PlayerId, Is.EqualTo("player-created"));
            Assert.That(http.SignInCount, Is.EqualTo(1));
            Assert.That(http.RefreshCount, Is.Zero);
            Assert.That(store.Session.PlayerId, Is.EqualTo("player-created"));
            Assert.That(store.Session.RefreshToken, Is.EqualTo("refresh-created"));
        }

        [Test]
        public void StoredSession_IsRefreshedWithoutCreatingAnotherPlayer()
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData("player-stored", "refresh-stored")
            };
            using var session = CreateSession(http, store);

            var token = session.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(token, Is.EqualTo("access-refreshed"));
            Assert.That(session.PlayerId, Is.EqualTo("player-stored"));
            Assert.That(http.SignInCount, Is.Zero);
            Assert.That(http.RefreshCount, Is.EqualTo(1));
            Assert.That(store.Session.RefreshToken, Is.EqualTo("refresh-rotated"));
            Assert.That(store.Session.SessionKind, Is.EqualTo(PlayServSessionKind.Anonymous));
        }

        [Test]
        public void UnauthorizedStoredSession_IsClearedAndRecreated()
        {
            var http = new FakeHttpClient { RejectRefreshAsUnauthorized = true };
            var store = new FakeSessionStore
            {
                Session = new PlayServPlayerSessionData("player-stored", "refresh-stored")
            };
            using var session = CreateSession(http, store);

            var token = session.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(token, Is.EqualTo("access-created"));
            Assert.That(session.PlayerId, Is.EqualTo("player-created"));
            Assert.That(http.RefreshCount, Is.EqualTo(1));
            Assert.That(http.SignInCount, Is.EqualTo(1));
            Assert.That(store.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void Coordinator_UsesCustomProviderInsteadOfAnonymousSession()
        {
            var http = new FakeHttpClient();
            var customProvider = new PlayServDelegateRuntimeTokenProvider(
                _ => Task.FromResult("custom.jwt"));
            var settings = CreateSettings();
            settings.RuntimeTokenProvider = customProvider;
            using var coordinator = CreateCoordinator(http, new FakeSessionStore());

            var prepared = coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            Assert.That(prepared.RuntimeTokenProvider, Is.SameAs(customProvider));
            Assert.That(prepared.PlayerId, Is.Empty);
            Assert.That(http.SignInCount, Is.Zero);
            Assert.That(http.RefreshCount, Is.Zero);
        }

        [Test]
        public void Coordinator_AutomaticSessionPopulatesRuntimePlayerId()
        {
            var http = new FakeHttpClient();
            var settings = CreateSettings();
            using var coordinator = CreateCoordinator(http, new FakeSessionStore());

            var prepared = coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            Assert.That(prepared.RuntimeTokenProvider, Is.Not.Null);
            Assert.That(prepared.PlayerId, Is.EqualTo("player-created"));
        }

        [Test]
        public void Coordinator_SwitchingToCustomProviderClearsManagedPlayerIdentity()
        {
            var http = new FakeHttpClient();
            var settings = CreateSettings();
            using var coordinator = CreateCoordinator(http, new FakeSessionStore());

            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
            Assert.That(settings.PlayerId, Is.EqualTo("player-created"));

            var customProvider = new PlayServDelegateRuntimeTokenProvider(
                _ => Task.FromResult("custom.jwt"));
            settings.RuntimeTokenProvider = customProvider;
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();

            Assert.That(settings.PlayerId, Is.Empty);
            Assert.That(settings.RuntimeTokenProvider, Is.SameAs(customProvider));
        }

        [Test]
        public void SessionScope_UsesHashedClientTokenWhenDeploymentIdIsAbsent()
        {
            var http = new FakeHttpClient();
            var store = new FakeSessionStore();
            var settings = CreateSettings();
            settings.DeploymentGameId = string.Empty;
            using var session = new PlayServPlayerSession(
                settings,
                http,
                store,
                SynchronizationContext.Current);

            session.GetTokenAsync().GetAwaiter().GetResult();

            Assert.That(store.LastScopeKey, Does.EndWith(".example.test"));
            Assert.That(store.LastScopeKey, Does.Not.Contain(settings.ClientToken));
            Assert.That(store.LastScopeKey.Split('.')[0], Has.Length.EqualTo(64));
        }

        [TestCase("wss://platform-monolith-ag3jh77tma-ew.a.run.app/ws", "wss://dashboard.dev.playserv.com/ws", true)]
        [TestCase("wss://platform-monolith-ag3jh77tma-ew.a.run.app/ws", "wss://dashboard.dev.playserv.com/ws", false)]
        [TestCase("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com/ws", true)]
        [TestCase("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com/ws", false)]
        public void DashboardUpgrade_RestoresTheSamePlayerAndRotatesItsStoredRefreshToken(string previous, string current, bool hasDeploymentId)
        {
            WithEndpointSessions(previous, current, hasDeploymentId, (settings, store, oldScope, newScope) =>
            {
                var http = new FakeHttpClient();
                store.SaveAsync(oldScope, new PlayServPlayerSessionData("player-existing", "refresh-existing")).GetAwaiter().GetResult();
                using (var session = new PlayServPlayerSession(settings, http, store, SynchronizationContext.Current))
                {
                    Assert.That(session.GetTokenAsync().GetAwaiter().GetResult(), Is.EqualTo("access-refreshed"));
                    Assert.That(session.PlayerId, Is.EqualTo("player-existing"));
                    Assert.That(http.SignInCount, Is.Zero);
                }
                Assert.That(store.LoadAsync(oldScope).GetAwaiter().GetResult().RefreshToken, Is.EqualTo("refresh-rotated"));
                Assert.That(store.LoadAsync(newScope).GetAwaiter().GetResult(), Is.Null);
                using (var reopened = new PlayServPlayerSession(settings, http, store, SynchronizationContext.Current))
                {
                    reopened.GetTokenAsync().GetAwaiter().GetResult();
                    Assert.That(reopened.PlayerId, Is.EqualTo("player-existing"));
                    Assert.That(http.LastRefreshToken, Is.EqualTo("refresh-rotated"));
                    reopened.LogoutAsync().GetAwaiter().GetResult();
                }
                using var afterLogout = new PlayServPlayerSession(settings, http, store, SynchronizationContext.Current);
                afterLogout.GetTokenAsync().GetAwaiter().GetResult();
                Assert.That(afterLogout.PlayerId, Is.EqualTo("player-created"));
            });
        }

        [TestCase("wss://platform-monolith-ag3jh77tma-ew.a.run.app/ws", "wss://dashboard.dev.playserv.com/ws")]
        [TestCase("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com/ws")]
        public void DashboardUpgrade_PrefersCurrentPlayerAndLogoutCannotResurrectTheOldScope(string previous, string current)
        {
            WithEndpointSessions(previous, current, false, (settings, store, oldScope, newScope) =>
            {
                store.SaveAsync(oldScope, new PlayServPlayerSessionData("player-old", "refresh-old")).GetAwaiter().GetResult();
                store.SaveAsync(newScope, new PlayServPlayerSessionData("player-current", "refresh-current")).GetAwaiter().GetResult();
                var http = new FakeHttpClient();
                using (var session = new PlayServPlayerSession(settings, http, store, SynchronizationContext.Current))
                {
                    session.GetTokenAsync().GetAwaiter().GetResult();
                    Assert.That(session.PlayerId, Is.EqualTo("player-current"));
                    Assert.That(http.LastRefreshToken, Is.EqualTo("refresh-current"));
                    http.RejectAnonymous = true;
                    session.LogoutAsync().GetAwaiter().GetResult();
                    Assert.That(store.LoadAsync(oldScope).GetAwaiter().GetResult(), Is.Null);
                    Assert.That(store.LoadAsync(newScope).GetAwaiter().GetResult(), Is.Null);
                }
                http.RejectAnonymous = false;
                using var restarted = new PlayServPlayerSession(settings, http, store, SynchronizationContext.Current);
                restarted.GetTokenAsync().GetAwaiter().GetResult();
                Assert.That(restarted.PlayerId, Is.EqualTo("player-created"));
            });
        }

        [TestCase("wss://platform.playserv.com/ws", "wss://dashboard.dev.playserv.com/ws")]
        [TestCase("wss://platform-monolith-ag3jh77tma-ew.a.run.app/ws", "wss://dashboard.playserv.com/ws")]
        [TestCase("wss://platform.playserv.com/ws", "wss://custom.example.test/ws")]
        [TestCase("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com/ws?tenant=custom")]
        [TestCase("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com:8443/ws")]
        public void DashboardUpgrade_DoesNotImportOtherEnvironmentsOrCustomEndpointSessions(string previous, string current)
        {
            WithEndpointSessions(previous, current, false, (settings, store, oldScope, newScope) =>
            {
                store.SaveAsync(oldScope, new PlayServPlayerSessionData("player-unrelated", "refresh-unrelated")).GetAwaiter().GetResult();
                var http = new FakeHttpClient();
                using var session = new PlayServPlayerSession(settings, http, store, SynchronizationContext.Current);
                session.GetTokenAsync().GetAwaiter().GetResult();
                Assert.That(session.PlayerId, Is.EqualTo("player-created"));
                Assert.That(http.RefreshCount, Is.Zero);
                Assert.That(store.LoadAsync(oldScope).GetAwaiter().GetResult().PlayerId, Is.EqualTo("player-unrelated"));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DashboardUpgrade_StorageFailureDoesNotCreateAReplacementPlayer(bool failLoad)
        {
            WithEndpointSessions("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com/ws", false,
                (settings, store, oldScope, newScope) =>
            {
                store.SaveAsync(oldScope, new PlayServPlayerSessionData("player-existing", "refresh-existing")).GetAwaiter().GetResult();
                var http = new FakeHttpClient();
                var failing = new FailingScopeStore(store, oldScope, failLoad);
                using var session = new PlayServPlayerSession(settings, http, failing, SynchronizationContext.Current);
                Assert.Throws<PlayServSessionPersistenceException>(() => session.GetTokenAsync().GetAwaiter().GetResult());
                Assert.That(http.SignInCount, Is.Zero);
                Assert.That(store.LoadAsync(oldScope).GetAwaiter().GetResult().RefreshToken, Is.EqualTo("refresh-existing"));
                Assert.That(store.LoadAsync(newScope).GetAwaiter().GetResult(), Is.Null);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DashboardUpgrade_FailedLegacyLogoutCleanupNeverSwitchesBackToTheOldPlayer(bool cancel)
        {
            WithEndpointSessions("wss://platform.playserv.com/ws", "wss://dashboard.playserv.com/ws", false,
                (settings, store, oldScope, newScope) =>
            {
                store.SaveAsync(oldScope, new PlayServPlayerSessionData("player-old", "refresh-old")).GetAwaiter().GetResult();
                store.SaveAsync(newScope, new PlayServPlayerSessionData("player-current", "refresh-current")).GetAwaiter().GetResult();
                var http = new FakeHttpClient();
                var failing = new FailingScopeStore(store, oldScope, false, failClear: true, cancel: cancel);
                using (var session = new PlayServPlayerSession(settings, http, failing, SynchronizationContext.Current))
                {
                    session.GetTokenAsync().GetAwaiter().GetResult();
                    if (cancel)
                        Assert.Throws<OperationCanceledException>(() => session.LogoutAsync().GetAwaiter().GetResult());
                    else
                        Assert.That(session.LogoutAsync().GetAwaiter().GetResult().IsSuccess, Is.False);
                }
                Assert.That(store.LoadAsync(newScope).GetAwaiter().GetResult(), Is.Not.Null);
                using var restarted = new PlayServPlayerSession(settings, http, store, SynchronizationContext.Current);
                restarted.GetTokenAsync().GetAwaiter().GetResult();
                Assert.That(restarted.PlayerId, Is.EqualTo("player-current"));
            });
        }

        private sealed class FailingScopeStore : IPlayServPlayerSessionStore
        {
            private readonly IPlayServPlayerSessionStore _store;
            private readonly string _scope;
            private readonly bool _failLoad;
            private readonly bool _failClear;
            private readonly bool _cancel;
            public FailingScopeStore(IPlayServPlayerSessionStore store, string scope, bool failLoad, bool failClear = false, bool cancel = false)
            {
                _store = store;
                _scope = scope;
                _failLoad = failLoad;
                _failClear = failClear;
                _cancel = cancel;
            }
            public Task<PlayServPlayerSessionData> LoadAsync(string scopeKey, CancellationToken cancellationToken = default)
            {
                if (_failLoad && scopeKey == _scope) throw new System.IO.IOException("Fixture storage failure.");
                return _store.LoadAsync(scopeKey, cancellationToken);
            }
            public Task SaveAsync(string scopeKey, PlayServPlayerSessionData session, CancellationToken cancellationToken = default)
            {
                if (!_failLoad && !_failClear && scopeKey == _scope) throw new System.IO.IOException("Fixture storage failure.");
                return _store.SaveAsync(scopeKey, session, cancellationToken);
            }
            public Task ClearAsync(string scopeKey, CancellationToken cancellationToken = default)
            {
                if (_failClear && scopeKey == _scope)
                {
                    if (_cancel) throw new OperationCanceledException();
                    throw new System.IO.IOException("Fixture cleanup failure.");
                }
                return _store.ClearAsync(scopeKey, cancellationToken);
            }
        }

        private static void WithEndpointSessions(string previous, string current, bool hasDeploymentId,
            Action<PlayServSettings, PlayServPlayerPrefsSessionStore, string, string> test)
        {
            var settings = CreateSettings();
            var id = Guid.NewGuid().ToString("N");
            settings.ClientToken = "pk_endpoint_fixture_" + id;
            settings.DeploymentGameId = hasDeploymentId ? "endpoint-fixture-" + id : string.Empty;
            settings.BackendServerAddress = previous;
            var oldScope = PlayServPlayerSession.BuildScopeKey(settings);
            settings.BackendServerAddress = current;
            var newScope = PlayServPlayerSession.BuildScopeKey(settings);
            var store = new PlayServPlayerPrefsSessionStore();
            try { test(settings, store, oldScope, newScope); }
            finally
            {
                store.ClearAsync(oldScope).GetAwaiter().GetResult();
                store.ClearAsync(newScope).GetAwaiter().GetResult();
            }
        }

        private static PlayServPlayerSession CreateSession(
            IPlayServRuntimeHttpClient httpClient,
            IPlayServPlayerSessionStore sessionStore)
        {
            return new PlayServPlayerSession(
                CreateSettings(),
                httpClient,
                sessionStore,
                SynchronizationContext.Current);
        }

        private static PlayServPlayerAuthCoordinator CreateCoordinator(
            IPlayServRuntimeHttpClient httpClient,
            IPlayServPlayerSessionStore sessionStore)
        {
            return new PlayServPlayerAuthCoordinator(
                _ => httpClient,
                sessionStore,
                CreateSettings,
                () => PlayServState.Offline,
                (_, __) => Task.FromResult(true),
                () => { },
                () => Task.FromResult(true));
        }

        private static PlayServSettings CreateSettings()
        {
            return new PlayServSettings
            {
                ClientToken = "pk_public",
                DeploymentGameId = "test-game",
                GameVersion = "1.0.0",
                BackendServerAddress = "wss://example.test/ws",
                EnableAutomaticPlayerFingerprint = false
            };
        }

        private sealed class FakeSessionStore : IPlayServPlayerSessionStore
        {
            public PlayServPlayerSessionData Session { get; set; }

            public int ClearCount { get; private set; }

            public string LastScopeKey { get; private set; }

            public Task<PlayServPlayerSessionData> LoadAsync(
                string scopeKey,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastScopeKey = scopeKey;
                return Task.FromResult(Session);
            }

            public Task SaveAsync(
                string scopeKey,
                PlayServPlayerSessionData session,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastScopeKey = scopeKey;
                Session = session;
                return Task.CompletedTask;
            }

            public Task ClearAsync(
                string scopeKey,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastScopeKey = scopeKey;
                Session = null;
                ClearCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeHttpClient : IPlayServRuntimeHttpClient
        {
            public int SignInCount { get; private set; }

            public int RefreshCount { get; private set; }

            public bool RejectRefreshAsUnauthorized { get; set; }
            public bool RejectAnonymous { get; set; }
            public string LastRefreshToken { get; private set; }

            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default)
            {
                return Task.FromResult("1.0.0");
            }

            public Task<PlayerTokenBundleDto> SignInAnonAsync(
                string clientToken,
                CancellationToken ct = default)
            {
                SignInCount++;
                if (RejectAnonymous) throw new InvalidOperationException("Fixture anonymous sign-in unavailable.");
                return Task.FromResult(new PlayerTokenBundleDto
                {
                    player_id = "player-created",
                    access_token = "access-created",
                    refresh_token = "refresh-created",
                    expires_in = 3600,
                    issued_at = DateTimeOffset.UtcNow.ToString("O")
                });
            }

            public Task<PlayerRefreshResponseDto> RefreshAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default)
            {
                RefreshCount++;
                LastRefreshToken = refreshToken;
                if (RejectRefreshAsUnauthorized)
                    throw new PlayServRuntimeHttpException(
                        "Request failed. HTTP 401.",
                        401,
                        string.Empty,
                        "unauthenticated",
                        isNetworkError: false);

                return Task.FromResult(new PlayerRefreshResponseDto
                {
                    access_token = "access-refreshed",
                    refresh_token = "refresh-rotated",
                    expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
                });
            }

            public Task<PlayerTokenBundleDto> LoginExternalAsync(
                string clientToken,
                PlayerExternalLoginRequestDto request,
                string playerAccessToken = null,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task SignOutAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
