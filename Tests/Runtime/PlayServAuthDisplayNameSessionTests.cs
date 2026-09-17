using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Identity;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServAuthSessionTests
    {
        [Test]
        public void DisplayName_settings_are_snapshotted_but_not_session_identity_or_persisted_credentials()
        {
            Assert.That(new PlayServSettings().AnonymousDisplayName, Is.Null);
            var settings = Settings();
            var scope = PlayServPlayerSession.BuildScopeKey(settings);
            settings.AnonymousDisplayName = "  Player 🚀  ";
            Assert.That(settings.Clone().AnonymousDisplayName, Is.EqualTo(settings.AnonymousDisplayName));
            Assert.That(PlayServPlayerSession.BuildScopeKey(settings), Is.EqualTo(scope));
            var http = new NamedAnonymousHttpClient();
            var store = new FakeSessionStore();
            using var session = CreateSession(http, store, settings);
            settings.AnonymousDisplayName = "Changed after creation";
            Assert.That(session.Matches(settings, store), Is.False);
            session.GetTokenAsync().GetAwaiter().GetResult();
            Assert.That(http.NamedRequests[0].display_name, Is.EqualTo("Player 🚀"));
            var serialized = new NewtonsoftJsonCodec().Serialize(store.Session);
            Assert.That(serialized, Does.Not.Contain("Player 🚀"));
            Assert.That(serialized, Does.Not.Contain("display_name"));
            Assert.That(new NewtonsoftJsonCodec().Serialize(settings.ToRuntimeSettings()),
                Does.Not.Contain("AnonymousDisplayName"));
            // A different configured name must not mint another player when restoring the store.
            using var restored = CreateSession(http, store, settings);
            restored.GetTokenAsync().GetAwaiter().GetResult();
            restored.RotateAccessTokenAsync().GetAwaiter().GetResult();
            Assert.That(restored.PlayerId, Is.EqualTo(session.PlayerId));
            Assert.That(http.AnonymousSignInCount, Is.EqualTo(1));
            Assert.That(http.RefreshCount, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisplayName_fresh_and_fallback_anonymous_requests_keep_name_and_fingerprint(bool logout)
        {
            var settings = Settings();
            settings.AnonymousDisplayName = "  Guest  ";
            settings.PlayerFingerprintProvider = new FakeFingerprintProvider();
            var http = new NamedAnonymousHttpClient();
            var store = new FakeSessionStore();
            if (!logout)
            {
                store.Session = new PlayServPlayerSessionData("previous", "refresh", PlayServSessionKind.Registered, null);
                http.RefreshException = new PlayServRuntimeHttpException("rejected", 401, "{}", "unauthorized", false);
            }
            using var session = CreateSession(http, store, settings);
            if (logout) session.LogoutAsync().GetAwaiter().GetResult();
            else session.GetTokenAsync().GetAwaiter().GetResult();
            Assert.That(http.NamedRequests.Count, Is.EqualTo(1));
            Assert.That(http.NamedRequests[0].display_name, Is.EqualTo("Guest"));
            Assert.That(http.NamedRequests[0].fingerprint.stable["platform"], Is.EqualTo("windows"));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DisplayName_proof_flows_to_login_and_link_but_not_anonymous_bootstrap(bool link, bool recover)
        {
            var http = new NamedAnonymousHttpClient();
            using var session = CreateSession(http, new FakeSessionStore());
            var proof = Proof().WithDisplayName("  Chosen name  ");
            var result = link ? session.LinkIdentityAsync(proof).GetAwaiter().GetResult() :
                session.LoginExternalAsync(proof, recover ? PlayServExternalLoginMode.RecoverProviderAccount :
                    PlayServExternalLoginMode.PreserveCurrentPlayer).GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(link ? http.LastLinkRequest.display_name : http.LastLoginRequest.display_name,
                Is.EqualTo("Chosen name"));
            Assert.That(http.NamedRequests.Count, Is.Zero);
            Assert.That(http.AnonymousSignInCount, Is.EqualTo(recover ? 0 : 1));
        }

        [Test]
        public void DisplayName_anonymous_and_provider_names_remain_independent_and_merge_has_no_name()
        {
            var settings = Settings();
            settings.AnonymousDisplayName = "Guest";
            var http = new NamedAnonymousHttpClient();
            using var session = CreateSession(http, new FakeSessionStore(), settings);
            var proof = Proof().WithDisplayName("Provider choice");
            session.LinkIdentityAsync(proof).GetAwaiter().GetResult();
            Assert.That(http.NamedRequests[0].display_name, Is.EqualTo("Guest"));
            Assert.That(http.LastLinkRequest.display_name, Is.EqualTo("Provider choice"));
            session.MergeIdentityAsync(MergeConflict(session.PlayerId), PlayServMergeChoice.KeepCurrentPlayer, proof)
                .GetAwaiter().GetResult();
            var json = new NewtonsoftJsonCodec().Serialize(http.LastMergeRequest);
            Assert.That(json, Does.Not.Contain("display_name"));
            Assert.That(json, Does.Not.Contain("Provider choice"));
            Assert.That(json, Does.Contain("provider_token"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisplayName_legacy_HTTP_warns_once_without_value_and_keeps_signin_working(bool withFingerprint)
        {
            var settings = Settings();
            settings.AnonymousDisplayName = "private cosmetic text";
            settings.PlayerFingerprintProvider = withFingerprint ? new FakeFingerprintProvider() : null;
            var http = new FakeHttpClient();
            http.AnonymousResponses.Enqueue(Bundle("second", "access2", "refresh2"));
            var warnings = new List<string>();
            var messages = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (message.Contains("anonymous display names")) warnings.Add(message);
                messages.Add(message);
            };
            Application.logMessageReceived += capture;
            try
            {
                using var session = CreateSession(http, new FakeSessionStore(), settings);
                session.GetTokenAsync().GetAwaiter().GetResult();
                var result = session.LogoutAsync().GetAwaiter().GetResult();
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(http.AnonymousSignInCount, Is.EqualTo(2));
                Assert.That(http.LastAnonymousFingerprint != null, Is.EqualTo(withFingerprint));
                foreach (var message in messages)
                    Assert.That(message, Does.Not.Contain("private cosmetic text"));
#if !PLAYSERV_DISABLE_LOGS
                Assert.That(warnings.Count, Is.EqualTo(1));
#endif
            }
            finally { Application.logMessageReceived -= capture; }
        }

        [Test]
        public void DisplayName_pre_cancellation_performs_no_auth_calls()
        {
            var settings = Settings();
            settings.AnonymousDisplayName = "Guest";
            var http = new NamedAnonymousHttpClient();
            using var session = CreateSession(http, new FakeSessionStore(), settings);
            var ct = new CancellationToken(true);
            Assert.Catch<OperationCanceledException>(() => session.GetTokenAsync(ct).GetAwaiter().GetResult());
            Assert.Catch<OperationCanceledException>(() => session.LoginExternalAsync(Proof().WithDisplayName("Player"),
                PlayServExternalLoginMode.PreserveCurrentPlayer, ct).GetAwaiter().GetResult());
            Assert.Catch<OperationCanceledException>(() => session.LinkIdentityAsync(Proof(), ct).GetAwaiter().GetResult());
            Assert.That(http.NamedRequests, Is.Empty);
            Assert.That(http.AnonymousSignInCount, Is.Zero);
            Assert.That(http.ExternalLoginCount, Is.Zero);
            Assert.That(http.LastLinkRequest, Is.Null);
        }

        private sealed class NamedAnonymousHttpClient : FakeHttpClient, IPlayServAnonymousLoginHttpClient
        {
            internal readonly List<PlayerAnonymousLoginRequestDto> NamedRequests = new List<PlayerAnonymousLoginRequestDto>();

            public Task<PlayerTokenBundleDto> SignInAnonAsync(string clientToken,
                PlayerAnonymousLoginRequestDto request, CancellationToken ct = default)
            {
                NamedRequests.Add(request);
                return base.SignInAnonAsync(clientToken, request.fingerprint, ct);
            }
        }
    }
}
