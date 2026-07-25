using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.AppleSignIn;
using UnityEngine;

namespace Playserv.Tests.Runtime.AppleSignIn
{
    public sealed class PlayServAppleSignInApiTests
    {
        private PlayServAppleSignInSettings _settings;
        private TestAppleProvider _provider;
        private PlayServAppleSignInApi _api;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<PlayServAppleSignInSettings>();
            _provider = new TestAppleProvider { IsAvailable = true };
            _api = new PlayServAppleSignInApi(_provider, () => _settings);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void SignInAsync_MapsRequestAndReturnsProviderCredential()
        {
            var request = new PlayServAppleSignInRequest(
                PlayServAppleSignInScope.Email,
                "nonce",
                "state");

            var task = _api.SignInAsync(request);
            var credential = CreateCredential("apple-user");
            _provider.CompleteSignIn(credential);

            Assert.That(task.GetAwaiter().GetResult(), Is.SameAs(credential));
            Assert.That(_provider.LastScopes, Is.EqualTo((int)PlayServAppleSignInScope.Email));
            Assert.That(_provider.LastNonce, Is.EqualTo("nonce"));
            Assert.That(_provider.LastState, Is.EqualTo("state"));
            Assert.That(_provider.QuickLoginRequested, Is.False);
        }

        [Test]
        public void SignInAsync_CancellationRemovesPendingRequest()
        {
            using var cancellation = new CancellationTokenSource();
            var task = _api.SignInAsync(ct: cancellation.Token);

            cancellation.Cancel();

            Assert.That(task.IsCanceled, Is.True);
            Assert.DoesNotThrow(() => _provider.CompleteSignIn(CreateCredential("late-user")));
        }

        [Test]
        public void QuickLoginAndCredentialState_UseProviderCallbacks()
        {
            var quickLogin = _api.QuickLoginAsync();
            _provider.CompleteSignIn(CreateCredential("quick-user"));
            Assert.That(quickLogin.GetAwaiter().GetResult().UserId, Is.EqualTo("quick-user"));
            Assert.That(_provider.QuickLoginRequested, Is.True);

            var stateTask = _api.GetCredentialStateAsync("  quick-user  ");
            var state = new PlayServAppleCredentialStateResult(
                PlayServAppleCredentialState.Authorized,
                0,
                string.Empty);
            _provider.CompleteCredentialState(state);

            Assert.That(stateTask.GetAwaiter().GetResult(), Is.SameAs(state));
            Assert.That(_provider.LastUserId, Is.EqualTo("quick-user"));
        }

        [Test]
        public void CredentialsRevoked_IsForwarded()
        {
            var callbackCount = 0;
            _api.CredentialsRevoked += () => callbackCount++;

            _provider.RaiseCredentialsRevoked();

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(_provider.RevocationCallbackEnabled, Is.True);
        }

        private static PlayServAppleSignInCredential CreateCredential(string userId)
        {
            return new PlayServAppleSignInCredential(
                PlayServAppleCredentialType.AppleId,
                userId,
                "user@example.test",
                "Test User",
                "Test",
                "User",
                "identity-token",
                "authorization-code",
                "state",
                string.Empty);
        }

        private sealed class TestAppleProvider : IPlayServAppleSignInProvider
        {
            public event Action<int, PlayServAppleSignInCredential> CredentialReceived;
            public event Action<int, int, string> SignInFailed;
            public event Action<int, PlayServAppleCredentialStateResult> CredentialStateReceived;
            public event Action CredentialsRevoked;

            public bool IsAvailable { get; set; }
            public int LastRequestId { get; private set; }
            public int LastScopes { get; private set; }
            public string LastNonce { get; private set; }
            public string LastState { get; private set; }
            public string LastUserId { get; private set; }
            public bool QuickLoginRequested { get; private set; }
            public bool RevocationCallbackEnabled { get; private set; }

            public void SignIn(int requestId, int scopes, string nonce, string state)
            {
                RecordSignIn(requestId, scopes, nonce, state, quickLogin: false);
            }

            public void QuickLogin(int requestId, int scopes, string nonce, string state)
            {
                RecordSignIn(requestId, scopes, nonce, state, quickLogin: true);
            }

            public void GetCredentialState(int requestId, string userId)
            {
                LastRequestId = requestId;
                LastUserId = userId;
            }

            public void SetCredentialsRevokedCallbackEnabled(bool enabled)
            {
                RevocationCallbackEnabled = enabled;
            }

            public void CompleteSignIn(PlayServAppleSignInCredential credential)
            {
                CredentialReceived?.Invoke(LastRequestId, credential);
            }

            public void CompleteCredentialState(PlayServAppleCredentialStateResult result)
            {
                CredentialStateReceived?.Invoke(LastRequestId, result);
            }

            public void RaiseCredentialsRevoked()
            {
                CredentialsRevoked?.Invoke();
            }

            private void RecordSignIn(
                int requestId,
                int scopes,
                string nonce,
                string state,
                bool quickLogin)
            {
                LastRequestId = requestId;
                LastScopes = scopes;
                LastNonce = nonce;
                LastState = state;
                QuickLoginRequested = quickLogin;
            }
        }
    }
}
