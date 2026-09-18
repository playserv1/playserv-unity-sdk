using System;
using System.Collections;
using UnityEngine.TestTools;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Matchmaking;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServAuthSessionTests
    {
        private static IEnumerator RunGameConnectionCheck(Func<Task> action)
        {
            var task = action();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Direct_connector_uses_managed_refresh_without_anonymous_fallback()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Direct_connector_uses_managed_refresh_without_anonymous_fallbackAsync();
            });
        }

        private async Task Direct_connector_uses_managed_refresh_without_anonymous_fallbackAsync()
        {
            var http = new FakeHttpClient();
            var settings = Settings();
            using var session = CreateSession(http, new FakeSessionStore(), settings);
            await session.GetTokenAsync();
            settings.PlayerId = session.PlayerId;
            settings.RuntimeTokenProvider = session;
            typeof(PlayServPlayerSession).GetField("_accessTokenExpiresAtUtc", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, DateTimeOffset.MinValue);
            http.RefreshException = new PlayServRuntimeHttpException("private error text", 401, "{}", "unauthorized", false);
            int sockets = 0;
            using var connection = new PlayServGameConnection(null, () => settings,
                _ => { sockets++; throw new InvalidOperationException(); }, new NewtonsoftJsonCodec(), SynchronizationContext.Current);
            var reservation = new PlayServMatchReservation("room", "reservation-secret", DateTimeOffset.MinValue,
                new PlayServRoomConnect("game.example", 443, "wss", "wss://game.example/", null), null, null, 60, () => 0, 0);
            try { await connection.ConnectAsync(reservation); Assert.Fail(); }
            catch (PlayServGameConnectionException error)
            {
                Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Unauthorized));
                Assert.That(error.ToString(), Does.Not.Contain("private error text"));
                Assert.That(error.InnerException, Is.Null);
            }
            Assert.That(http.AnonymousSignInCount, Is.EqualTo(1));
            Assert.That(http.RefreshCount, Is.EqualTo(1));
            Assert.That(sockets, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Game_connection_token_refresh_never_creates_a_replacement_anonymous_player()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Game_connection_token_refresh_never_creates_a_replacement_anonymous_playerAsync();
            });
        }

        private async Task Game_connection_token_refresh_never_creates_a_replacement_anonymous_playerAsync()
        {
            var http = new FakeHttpClient();
            using var session = CreateSession(http, new FakeSessionStore());
            await session.GetTokenAsync();
            typeof(PlayServPlayerSession).GetField("_accessTokenExpiresAtUtc", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, DateTimeOffset.MinValue);
            http.RefreshException = new PlayServRuntimeHttpException("secret server payload", 401, "{}", "unauthorized", false);
            var method = typeof(PlayServPlayerSession).GetMethod("GetExistingPlayerTokenAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Direct connection requires refresh without anonymous fallback.");
            try
            {
                await (Task<string>)method.Invoke(session, new object[] { CancellationToken.None });
                Assert.Fail("Rejected refresh must fail the current connection, not replace its identity.");
            }
            catch (PlayServSessionRejectedException) { }
            Assert.That(http.AnonymousSignInCount, Is.EqualTo(1));
            Assert.That(http.RefreshCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Game_connection_token_helper_reuses_fresh_token_and_refreshes_the_same_player()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Game_connection_token_helper_reuses_fresh_token_and_refreshes_the_same_playerAsync();
            });
        }

        private async Task Game_connection_token_helper_reuses_fresh_token_and_refreshes_the_same_playerAsync()
        {
            var http = new FakeHttpClient();
            using var session = CreateSession(http, new FakeSessionStore());
            var first = await session.GetTokenAsync();
            var id = session.PlayerId;
            var method = typeof(PlayServPlayerSession).GetMethod("GetExistingPlayerTokenAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That(await (Task<string>)method.Invoke(session, new object[] { CancellationToken.None }), Is.EqualTo(first));
            Assert.That(http.RefreshCount, Is.Zero);
            typeof(PlayServPlayerSession).GetField("_accessTokenExpiresAtUtc", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, DateTimeOffset.MinValue);
            await (Task<string>)method.Invoke(session, new object[] { CancellationToken.None });
            Assert.That(session.PlayerId, Is.EqualTo(id));
            Assert.That(http.RefreshCount, Is.EqualTo(1));
            Assert.That(http.AnonymousSignInCount, Is.EqualTo(1));
        }
    }
}
