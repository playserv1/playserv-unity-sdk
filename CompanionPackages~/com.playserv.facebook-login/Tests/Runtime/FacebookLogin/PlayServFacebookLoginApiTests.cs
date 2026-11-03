using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Identity;
using Playserv.Wrapper;

namespace Playserv.FacebookLogin.Tests
{
    public sealed class PlayServFacebookLoginApiTests
    {
        [Test]
        public void Request_NormalizesPermissionsAndKeepsNonce()
        {
            var request = new PlayServFacebookLoginRequest(
                new[] { " email ", "public_profile", "email" },
                " nonce ");

            Assert.That(request.Permissions, Is.EqualTo(new[] { "email", "public_profile" }));
            Assert.That(request.Nonce, Is.EqualTo("nonce"));
        }

        [Test]
        public void GenerateNonce_IsSecureBase64UrlShape()
        {
            var first = PlayServMetaUnityBridge.GenerateNonce();
            var second = PlayServMetaUnityBridge.GenerateNonce();

            Assert.That(first, Has.Length.EqualTo(43));
            Assert.That(first.All(value => char.IsLetterOrDigit(value) || value == '-' || value == '_'), Is.True);
            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void Credential_RequiresTokenAndNonceAndBuildsFacebookProof()
        {
            var complete = new PlayServFacebookCredential("jwt-token", "nonce");
            var missingNonce = new PlayServFacebookCredential("jwt-token", string.Empty);

            Assert.That(complete.TryCreateBackendProof(out var proof), Is.True);
            Assert.That(proof.ProviderId, Is.EqualTo(PlayServIdentityProviderIds.Facebook));
            Assert.That(proof.ProviderToken, Is.EqualTo("jwt-token"));
            Assert.That(proof.Nonce, Is.EqualTo("nonce"));
            Assert.That(missingNonce.TryCreateBackendProof(out _), Is.False);
        }

        [Test]
        public void LoginAsync_ForwardsRequestModeAndProof()
        {
            var request = new PlayServFacebookLoginRequest(new[] { "email" }, "nonce");
            var provider = new TestProvider
            {
                Credential = new PlayServFacebookCredential("jwt-token", "nonce")
            };
            PlayServExternalIdentityProof captured = null;
            PlayServExternalLoginMode capturedMode = default;
            var api = new PlayServFacebookLoginApi(
                provider,
                (proof, mode, _) =>
                {
                    captured = proof;
                    capturedMode = mode;
                    return Task.FromResult<PlayServAuthResult>(null);
                },
                (_, __) => Task.FromResult<PlayServAuthResult>(null));

            api.LoginAsync(request, PlayServExternalLoginMode.RecoverProviderAccount)
                .GetAwaiter().GetResult();

            Assert.That(provider.LastRequest, Is.SameAs(request));
            Assert.That(captured.ProviderToken, Is.EqualTo("jwt-token"));
            Assert.That(captured.Nonce, Is.EqualTo("nonce"));
            Assert.That(capturedMode, Is.EqualTo(PlayServExternalLoginMode.RecoverProviderAccount));
        }

        [Test]
        public void LinkAsync_ProviderFailureDoesNotInvokePlayServAuth()
        {
            var authCalls = 0;
            var api = new PlayServFacebookLoginApi(
                new TestProvider { Failure = new PlayServFacebookLoginException("login", "provider_rejected", "Rejected.") },
                (_, __, ___) =>
                {
                    authCalls++;
                    return Task.FromResult<PlayServAuthResult>(null);
                },
                (_, __) =>
                {
                    authCalls++;
                    return Task.FromResult<PlayServAuthResult>(null);
                });

            Assert.Throws<PlayServFacebookLoginException>(() => api.LinkAsync().GetAwaiter().GetResult());
            Assert.That(authCalls, Is.Zero);
        }

        [Test]
        public void MetaBridge_RequestsLimitedLoginAndAcceptsMatchingNonce()
        {
            var mobile = new Facebook.Unity.TestMobileFacebook();
            Facebook.Unity.FB.Implementation = mobile;
            Facebook.Unity.FB.IsInitialized = true;
            Assert.That(PlayServMetaUnityBridge.TryCreate(out var bridge), Is.True);
            Assert.That(bridge.IsInitialized, Is.True);

            var task = bridge.GetCredentialAsync(
                new PlayServFacebookLoginRequest(new[] { "email" }, "expected-nonce"),
                CancellationToken.None);
            Assert.That(mobile.Tracking, Is.EqualTo(Facebook.Unity.LoginTracking.LIMITED));
            Assert.That(mobile.Permissions, Is.EqualTo(new[] { "email" }));

            mobile.Complete("facebook-jwt", "expected-nonce");
            var credential = task.GetAwaiter().GetResult();
            Assert.That(credential.AuthenticationToken, Is.EqualTo("facebook-jwt"));
            Assert.That(credential.Nonce, Is.EqualTo("expected-nonce"));
        }

        [Test]
        public void MetaBridge_RejectsNonceMismatchBeforeCredentialCreation()
        {
            var mobile = new Facebook.Unity.TestMobileFacebook();
            Facebook.Unity.FB.Implementation = mobile;
            Facebook.Unity.FB.IsInitialized = true;
            PlayServMetaUnityBridge.TryCreate(out var bridge);

            var task = bridge.GetCredentialAsync(
                new PlayServFacebookLoginRequest(nonce: "expected-nonce"),
                CancellationToken.None);
            mobile.Complete("facebook-jwt", "different-nonce");

            var exception = Assert.Throws<PlayServFacebookLoginException>(() =>
                task.GetAwaiter().GetResult());
            Assert.That(exception.SourceCode, Is.EqualTo("nonce_mismatch"));
            Assert.That(exception.Message, Does.Not.Contain("facebook-jwt"));
        }

        [Test]
        public void MetaBridge_CancellationIgnoresLateProviderCallback()
        {
            var mobile = new Facebook.Unity.TestMobileFacebook();
            Facebook.Unity.FB.Implementation = mobile;
            Facebook.Unity.FB.IsInitialized = true;
            PlayServMetaUnityBridge.TryCreate(out var bridge);
            using var cancellation = new CancellationTokenSource();

            var task = bridge.GetCredentialAsync(new PlayServFacebookLoginRequest(), cancellation.Token);
            cancellation.Cancel();
            Assert.Throws<TaskCanceledException>(() => task.GetAwaiter().GetResult());

            Assert.DoesNotThrow(() => mobile.Complete("late-token", mobile.Nonce));
            Assert.That(task.IsCanceled, Is.True);
        }

        private sealed class TestProvider : IPlayServFacebookLoginProvider
        {
            public bool IsAvailable => true;
            public bool IsInitialized => true;
            public PlayServFacebookCredential Credential { get; set; }
            public PlayServFacebookLoginException Failure { get; set; }
            public PlayServFacebookLoginRequest LastRequest { get; private set; }

            public Task<PlayServFacebookCredential> GetCredentialAsync(
                PlayServFacebookLoginRequest request,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                LastRequest = request;
                if (Failure != null)
                    throw Failure;
                return Task.FromResult(Credential);
            }
        }
    }
}

namespace Facebook.Unity
{
    public enum LoginTracking
    {
        ENABLED = 0,
        LIMITED = 1
    }

    public delegate void FacebookDelegate<T>(T result);

    public interface ILoginResult
    {
        bool Cancelled { get; }
        string Error { get; }
    }

    public sealed class AuthenticationToken
    {
        public AuthenticationToken(string tokenString, string nonce)
        {
            TokenString = tokenString;
            Nonce = nonce;
        }

        public string TokenString { get; }
        public string Nonce { get; }
    }

    public sealed class TestLoginResult : ILoginResult
    {
        public bool Cancelled { get; set; }
        public string Error { get; set; }
    }

    public sealed class TestMobileFacebook
    {
        private FacebookDelegate<ILoginResult> _callback;
        private AuthenticationToken _token;

        public LoginTracking Tracking { get; private set; }
        public string[] Permissions { get; private set; }
        public string Nonce { get; private set; }

        public void LoginWithTrackingPreference(
            LoginTracking tracking,
            IEnumerable<string> permissions,
            string nonce,
            FacebookDelegate<ILoginResult> callback)
        {
            Tracking = tracking;
            Permissions = permissions.ToArray();
            Nonce = nonce;
            _callback = callback;
        }

        public AuthenticationToken CurrentAuthenticationToken() => _token;

        public void Complete(string token, string nonce)
        {
            _token = new AuthenticationToken(token, nonce);
            _callback?.Invoke(new TestLoginResult());
        }
    }

    public static class FB
    {
        public static TestMobileFacebook Implementation { get; set; }
        public static bool IsInitialized { get; set; }

        public sealed class Mobile
        {
            public static void LoginWithTrackingPreference(
                LoginTracking tracking,
                IEnumerable<string> permissions,
                string nonce,
                FacebookDelegate<ILoginResult> callback) =>
                Implementation.LoginWithTrackingPreference(tracking, permissions, nonce, callback);

            public static AuthenticationToken CurrentAuthenticationToken() =>
                Implementation.CurrentAuthenticationToken();
        }
    }
}
