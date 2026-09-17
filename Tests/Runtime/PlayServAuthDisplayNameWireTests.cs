using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Identity;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using UnityEngine.Networking;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServAuthDisplayNameWireTests
    {
        [TestCase("anon")]
        [TestCase("login")]
        [TestCase("link")]
        public void Auth_request_sends_literal_display_name_in_real_upload_bytes(string operation)
        {
            var dto = CreateDto(operation);
            // Reflection also lets this regression execute against the pre-feature DTOs.
            dto.GetType().GetField("display_name")?.SetValue(dto, "  Гравець 🚀  ");
            using var request = Build(operation, dto);
            var body = new NewtonsoftJsonCodec().Deserialize<Dictionary<string, object>>(
                Encoding.UTF8.GetString(request.uploadHandler.data));
            Assert.That(request.method, Is.EqualTo("POST"));
            Assert.That(request.url, Does.EndWith("/auth/players/" + operation));
            Assert.That(request.GetRequestHeader("X-Playserv-Client"), Is.EqualTo("pk_test"));
            if (operation != "anon")
                Assert.That(request.GetRequestHeader("Authorization"), Is.EqualTo("Bearer a.b.c"));
            else
                Assert.That(request.GetRequestHeader("Authorization"), Is.Null.Or.Empty);
            Assert.That(body.ContainsKey("display_name"), Is.True, "The literal backend wire key is required.");
            Assert.That(body["display_name"], Is.EqualTo("Гравець 🚀"));
            Assert.That(body.ContainsKey("displayName"), Is.False);
            Assert.That(body.ContainsKey("name"), Is.False);
            if (operation != "anon")
            {
                Assert.That(body["provider"], Is.EqualTo("google"));
                Assert.That(body["provider_token"], Is.EqualTo("provider-token"));
                Assert.That(body["nonce"], Is.EqualTo("nonce"));
                Assert.That(body["mode"], Is.EqualTo("test-mode"));
            }
        }

        [TestCase("anon")]
        [TestCase("login")]
        [TestCase("link")]
        public void Blank_names_are_omitted_and_old_bodies_are_preserved(string operation)
        {
            foreach (var name in new[] { null, "", " \t\r\n\u2003 " })
            {
                var dto = CreateDto(operation);
                dto.GetType().GetField("display_name").SetValue(dto, name);
                using var request = Build(operation, dto);
                var json = Encoding.UTF8.GetString(request.uploadHandler.data);
                Assert.That(json, Does.Not.Contain("display_name"));
                if (operation == "anon") Assert.That(json, Is.EqualTo("{}"));
                else Assert.That(json, Is.EqualTo("{\"provider\":\"google\",\"provider_token\":\"provider-token\"," +
                    "\"mode\":\"test-mode\",\"nonce\":\"nonce\"" + (operation == "login" ? ",\"fingerprint\":null}" : "}")));
            }
        }

        [TestCase("anon")]
        [TestCase("login")]
        [TestCase("link")]
        public void Wire_name_is_bounded_without_mutating_caller_request(string operation)
        {
            var dto = CreateDto(operation);
            var original = "  " + new string('x', 63) + "🚀 trailing  ";
            dto.GetType().GetField("display_name").SetValue(dto, original);
            var fingerprint = new PlayerFingerprintDto
            {
                stable = new Dictionary<string, object> { ["platform"] = "windows" }
            };
            dto.GetType().GetField("fingerprint")?.SetValue(dto, fingerprint);
            using var request = Build(operation, dto);
            // Mutating the original DTO after request construction cannot change its upload bytes.
            Assert.That(dto.GetType().GetField("display_name").GetValue(dto), Is.EqualTo(original));
            dto.GetType().GetField("display_name").SetValue(dto, "later");
            fingerprint.stable["platform"] = "later";
            var json = Encoding.UTF8.GetString(request.uploadHandler.data);
            var body = new NewtonsoftJsonCodec().Deserialize<Dictionary<string, object>>(json);
            Assert.That(body["display_name"], Is.EqualTo(new string('x', 63)));
            Assert.That(json, Does.Not.Contain("later"));
            if (operation != "link") Assert.That(json, Does.Contain("windows"));
        }

        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase(" \t\r\n\u2003 ", null)]
        [TestCase("  Гравець 🚀  ", "Гравець 🚀")]
        public void Shared_name_policy_trims_or_omits(string input, string expected) =>
            Assert.That(PlayServDisplayNamePolicy.Normalize(input), Is.EqualTo(expected));

        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(5000)]
        public void Shared_name_policy_truncates_not_rejects(int length) =>
            Assert.That(PlayServDisplayNamePolicy.Normalize(new string('x', length)),
                Is.EqualTo(new string('x', Math.Min(length, 64))));

        [Test]
        public void Shared_name_policy_preserves_surrogate_pairs_and_trims_new_trailing_space()
        {
            Assert.That(PlayServDisplayNamePolicy.Normalize(new string('x', 62) + "🚀z"),
                Is.EqualTo(new string('x', 62) + "🚀"));
            Assert.That(PlayServDisplayNamePolicy.Normalize(new string('x', 63) + "🚀z"),
                Is.EqualTo(new string('x', 63)));
            Assert.That(PlayServDisplayNamePolicy.Normalize(new string('x', 63) + " rest"),
                Is.EqualTo(new string('x', 63)));
        }

        [Test]
        public void Proof_copy_preserves_all_credential_forms_and_does_not_mutate_source()
        {
            foreach (var original in new[]
            {
                new PlayServExternalIdentityProof("google", "id-token", "auth-code"),
                new PlayServExternalIdentityProof("apple", null, "auth-code"),
                PlayServExternalIdentityProof.FromProviderToken("google", "token", "mode", "nonce"),
                PlayServExternalIdentityProof.FromEpicExternalAuthToken("exchange", true),
                PlayServExternalIdentityProof.FromSteamTicket("ticket"),
                PlayServExternalIdentityProof.FromFacebookLimitedLoginToken("auth-token", "nonce"),
                PlayServExternalIdentityProof.FromAppleIdToken("token", "nonce"),
                PlayServExternalIdentityProof.FromGoogleIdToken("token", "nonce"),
                PlayServExternalIdentityProof.FromPlayServToken("jwt")
            })
            {
                var copy = original.WithDisplayName("  Player  ");
                Assert.That(copy, Is.Not.SameAs(original));
                Assert.That(original.DisplayName, Is.Null);
                Assert.That(copy.DisplayName, Is.EqualTo("Player"));
                Assert.That(copy.ProviderId, Is.EqualTo(original.ProviderId));
                Assert.That(copy.ProviderToken, Is.EqualTo(original.ProviderToken));
                Assert.That(copy.IdToken, Is.EqualTo(original.IdToken));
                Assert.That(copy.AuthorizationCode, Is.EqualTo(original.AuthorizationCode));
                Assert.That(copy.Mode, Is.EqualTo(original.Mode));
                Assert.That(copy.Nonce, Is.EqualTo(original.Nonce));
                Assert.That(copy.WithDisplayName(null).DisplayName, Is.Null);
                Assert.That(copy.DisplayName, Is.EqualTo("Player"));
            }
        }

        [TestCase("anon")]
        [TestCase("login")]
        [TestCase("link")]
        public void Pre_cancellation_stops_real_HTTP_before_request_construction(string operation)
        {
            var client = CreateClient();
            var ct = new CancellationToken(true);
            Func<Task> call = operation == "anon"
                ? () => ((IPlayServAnonymousLoginHttpClient)client).SignInAnonAsync("invalid", null, ct)
                : operation == "login"
                    ? () => ((IPlayServRuntimeHttpClient)client).LoginExternalAsync("invalid", null, null, ct)
                    : () => ((IPlayServPlayerIdentityHttpClient)client).LinkIdentityAsync("invalid", null, null, ct);
            Assert.Throws<OperationCanceledException>(() => call().GetAwaiter().GetResult());
        }

        private static object CreateDto(string operation) => operation == "anon"
            ? (object)new PlayerAnonymousLoginRequestDto()
            : operation == "login"
                ? new PlayerExternalLoginRequestDto { provider = "google", provider_token = "provider-token", nonce = "nonce", mode = "test-mode" }
                : new PlayerLinkRequestDto { provider = "google", provider_token = "provider-token", nonce = "nonce", mode = "test-mode" };

        private static UnityWebRequest Build(string operation, object dto)
        {
            var client = CreateClient();
            var type = client.GetType();
            var method = operation == "anon" ? "CreateAnonymousSignInRequest" :
                operation == "login" ? "CreateExternalLoginRequest" : "CreateLinkIdentityRequest";
            return (UnityWebRequest)type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(client, operation == "anon" ? new[] { "pk_test", dto } : new[] { "pk_test", dto, "a.b.c" });
        }

        private static object CreateClient()
        {
            var type = typeof(IPlayServRuntimeHttpClient).Assembly.GetType(
                "Playserv.Http.Modules.Unity.UnityWebRequestRuntimeHttpClient", true);
            return Activator.CreateInstance(type, new object[]
            {
                new PlayServRuntimeSettings { BackendServerAddress = "https://auth.test" }, new NewtonsoftJsonCodec()
            });
        }
    }
}
