using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServCredentialSecurityTests
    {
        [Test]
        public void ClientToken_AcceptsOnlyPublicKey()
        {
            Assert.That(PlayServCredentialPolicy.NormalizeClientToken(" pk_public "), Is.EqualTo("pk_public"));
            Assert.Throws<InvalidOperationException>(
                () => PlayServCredentialPolicy.NormalizeClientToken("sk_secret"));
            Assert.Throws<InvalidOperationException>(
                () => PlayServCredentialPolicy.NormalizeClientToken("player.jwt.value"));
        }

        [Test]
        public void PlayerAuthorization_RejectsPlayServKeys()
        {
            Assert.That(
                PlayServCredentialPolicy.NormalizePlayerAuthorization("player.jwt.value"),
                Is.EqualTo("Bearer player.jwt.value"));
            Assert.Throws<InvalidOperationException>(
                () => PlayServCredentialPolicy.NormalizePlayerAuthorization("Bearer sk_secret"));
            Assert.Throws<InvalidOperationException>(
                () => PlayServCredentialPolicy.NormalizePlayerAuthorization("pk_public"));
        }

        [Test]
        public void DelegateProvider_ForwardsCancellationAndToken()
        {
            using var cancellation = new CancellationTokenSource();
            var observedToken = CancellationToken.None;
            var provider = new PlayServDelegateRuntimeTokenProvider(ct =>
            {
                observedToken = ct;
                return Task.FromResult("player.jwt.value");
            });

            var token = provider.GetTokenAsync(cancellation.Token).GetAwaiter().GetResult();

            Assert.That(token, Is.EqualTo("player.jwt.value"));
            Assert.That(observedToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void SettingsClone_PreservesProviderWithoutSerializedAuthorization()
        {
            var provider = new PlayServDelegateRuntimeTokenProvider(_ => Task.FromResult("player.jwt.value"));
            var settings = new PlayServSettings
            {
                ClientToken = "pk_public",
                RuntimeTokenProvider = provider
            };

            var clone = settings.Clone();

            Assert.That(clone.RuntimeTokenProvider, Is.SameAs(provider));
            Assert.That(typeof(PlayServConfig).GetProperty("Authorization"), Is.Null);
            Assert.That(typeof(PlayServConfig).GetProperty("DeployAuthToken"), Is.Null);
            Assert.That(typeof(PlayServConfig).GetProperty("PlayerAccessToken"), Is.Null);
        }

        [Test]
        public void SettingsClone_PreservesRuntimeOnlyPlayerAccessToken()
        {
            var settings = new PlayServSettings
            {
                PlayerAccessToken = "player.jwt.value"
            };

            var clone = settings.Clone();

            Assert.That(clone.PlayerAccessToken, Is.EqualTo("player.jwt.value"));
            Assert.That(clone.ToRuntimeSettings().PlayerAccessToken, Is.EqualTo("player.jwt.value"));
        }

        [Test]
        public void SettingsClone_PreservesAutomaticPlayerAuthenticationOptions()
        {
            var store = new TestPlayerSessionStore();
            var settings = new PlayServSettings
            {
                EnableAutomaticPlayerAuthentication = false,
                PlayerSessionStore = store
            };

            var clone = settings.Clone();

            Assert.That(clone.EnableAutomaticPlayerAuthentication, Is.False);
            Assert.That(clone.PlayerSessionStore, Is.SameAs(store));
        }

        private sealed class TestPlayerSessionStore : IPlayServPlayerSessionStore
        {
            public Task<PlayServPlayerSessionData> LoadAsync(
                string scopeKey,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<PlayServPlayerSessionData>(null);

            public Task SaveAsync(
                string scopeKey,
                PlayServPlayerSessionData session,
                CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task ClearAsync(
                string scopeKey,
                CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }
}
