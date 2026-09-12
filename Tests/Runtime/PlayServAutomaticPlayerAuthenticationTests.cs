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

            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default)
            {
                return Task.FromResult("1.0.0");
            }

            public Task<PlayerTokenBundleDto> SignInAnonAsync(
                string clientToken,
                CancellationToken ct = default)
            {
                SignInCount++;
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
