using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GoogleSignIn;
using UnityEngine;

namespace Playserv.Tests.Runtime.GoogleSignIn
{
    public sealed class PlayServGoogleSignInApiTests
    {
        private PlayServGoogleSignInSettings _settings;
        private TestGoogleProvider _provider;
        private PlayServGoogleSignInApi _api;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<PlayServGoogleSignInSettings>();
            _provider = new TestGoogleProvider();
            _api = new PlayServGoogleSignInApi(_provider, () => _settings);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void SignInAsync_ForwardsSettingsRequestAndCredential()
        {
            var request = new PlayServGoogleSignInRequest(webClientId: "client-id");
            var expected = CreateCredential("google-user");
            _provider.NextCredential = expected;

            var result = _api.SignInAsync(request).GetAwaiter().GetResult();

            Assert.That(result, Is.SameAs(expected));
            Assert.That(_provider.LastSettings, Is.SameAs(_settings));
            Assert.That(_provider.LastRequest, Is.SameAs(request));
            Assert.That(_provider.SilentRequested, Is.False);
        }

        [Test]
        public void SignInSilentlyAsync_UsesSilentProviderPath()
        {
            _provider.NextCredential = CreateCredential("silent-user");

            var result = _api.SignInSilentlyAsync().GetAwaiter().GetResult();

            Assert.That(result.UserId, Is.EqualTo("silent-user"));
            Assert.That(_provider.SilentRequested, Is.True);
        }

        [Test]
        public void SignInAsync_PreCanceledToken_DoesNotInvokeProvider()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var task = _api.SignInAsync(ct: cancellation.Token);

            Assert.That(task.IsCanceled, Is.True);
            Assert.That(_provider.SignInCallCount, Is.EqualTo(0));
        }

        [Test]
        public void SignOutAndDisconnect_AreForwarded()
        {
            _api.SignOut();
            _api.Disconnect();

            Assert.That(_provider.SignOutCount, Is.EqualTo(1));
            Assert.That(_provider.DisconnectCount, Is.EqualTo(1));
        }

        private static PlayServGoogleSignInCredential CreateCredential(string userId)
        {
            return new PlayServGoogleSignInCredential(
                userId,
                "user@example.test",
                "Test User",
                "Test",
                "User",
                "https://example.test/photo.png",
                "identity-token",
                "authorization-code");
        }

        private sealed class TestGoogleProvider : IPlayServGoogleSignInProvider
        {
            public bool IsAvailable => true;
            public PlayServGoogleSignInCredential NextCredential { get; set; }
            public PlayServGoogleSignInSettings LastSettings { get; private set; }
            public PlayServGoogleSignInRequest LastRequest { get; private set; }
            public bool SilentRequested { get; private set; }
            public int SignInCallCount { get; private set; }
            public int SignOutCount { get; private set; }
            public int DisconnectCount { get; private set; }

            public Task<PlayServGoogleSignInCredential> SignInAsync(
                PlayServGoogleSignInSettings settings,
                PlayServGoogleSignInRequest request,
                CancellationToken ct)
            {
                return Complete(settings, request, silent: false, ct);
            }

            public Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
                PlayServGoogleSignInSettings settings,
                PlayServGoogleSignInRequest request,
                CancellationToken ct)
            {
                return Complete(settings, request, silent: true, ct);
            }

            public void SignOut()
            {
                SignOutCount++;
            }

            public void Disconnect()
            {
                DisconnectCount++;
            }

            private Task<PlayServGoogleSignInCredential> Complete(
                PlayServGoogleSignInSettings settings,
                PlayServGoogleSignInRequest request,
                bool silent,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                SignInCallCount++;
                LastSettings = settings;
                LastRequest = request;
                SilentRequested = silent;
                return Task.FromResult(NextCredential);
            }
        }
    }
}
