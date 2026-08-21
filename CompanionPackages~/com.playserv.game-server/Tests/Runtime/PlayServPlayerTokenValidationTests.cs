using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Serialization;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServPlayerTokenValidationTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        private RSA _rsa;

        [SetUp]
        public void SetUp()
        {
            PlayServGameServer.SetSupportedBuildForTesting(true);
            PlayServGameServer.CancelForModuleShutdown();
            _rsa = RSA.Create();
            _rsa.KeySize = 2048;
        }

        [TearDown]
        public void TearDown()
        {
            _rsa.Dispose();
            PlayServGameServer.ConfigurePlayerTokenValidatorForTesting(null, null);
            PlayServGameServer.CancelForModuleShutdown();
            PlayServGameServer.SetSupportedBuildForTesting(null);
        }

        [Test]
        public void ValidRs256Token_ReturnsOnlySafeRuntimeClaims()
        {
            var jwks = new FakeJwksTransport(Jwks("kid-1", _rsa));
            Configure(jwks);
            var jwt = Token(_rsa, "kid-1");

            var result = Validate(jwt);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Claims.PlayerId, Is.EqualTo("plr_1"));
            Assert.That(result.Claims.ProjectId, Is.EqualTo("prj_1"));
            Assert.That(result.Claims.Environment, Is.EqualTo("prod"));
            Assert.That(result.Claims.SessionId, Is.EqualTo("psess_1"));
            Assert.That(result.Claims.TokenId, Is.EqualTo("jti_1"));
            Assert.That(result.Claims.Providers, Is.EquivalentTo(new[] { "google", "steam" }));
            Assert.That(jwks.CallCount, Is.EqualTo(1));
            Assert.That(jwks.LastUrl, Is.EqualTo("https://api.playserv.test/.well-known/jwks.json"));
        }

        [TestCase("HS256", "jwt_algorithm_not_allowed")]
        [TestCase("none", "jwt_algorithm_not_allowed")]
        public void NonRs256Algorithm_IsRejectedBeforeJwks(string algorithm, string expectedCode)
        {
            var jwks = new FakeJwksTransport(Jwks("kid-1", _rsa));
            Configure(jwks);

            var result = Validate(Token(_rsa, "kid-1", algorithm: algorithm));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.UnifiedError.SourceCode, Is.EqualTo(expectedCode));
            Assert.That(jwks.CallCount, Is.Zero);
        }

        [Test]
        public void UnknownKid_ForcesOneJwksRefresh()
        {
            using var oldKey = RSA.Create();
            oldKey.KeySize = 2048;
            var jwks = new FakeJwksTransport(
                Jwks("old", oldKey),
                Jwks("rotated", _rsa));
            Configure(jwks);

            var result = Validate(Token(_rsa, "rotated"));

            Assert.That(result.IsValid, Is.True);
            Assert.That(jwks.CallCount, Is.EqualTo(2));
        }

        [Test]
        public void UnknownKidAfterRefresh_ReturnsTypedFailure()
        {
            var jwks = new FakeJwksTransport(
                Jwks("other", _rsa),
                Jwks("other", _rsa));
            Configure(jwks);

            var result = Validate(Token(_rsa, "missing"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.UnifiedError.SourceCode, Is.EqualTo("jwt_kid_unknown"));
            Assert.That(jwks.CallCount, Is.EqualTo(2));
        }

        [TestCase("playserv-other", "prj_1", "prod", "jwt_issuer_mismatch")]
        [TestCase("playserv", "prj_other", "prod", "jwt_project_mismatch")]
        [TestCase("playserv", "prj_1", "dev", "jwt_environment_mismatch")]
        public void ScopeMismatch_IsRejected(
            string issuer,
            string project,
            string environment,
            string expectedCode)
        {
            Configure(new FakeJwksTransport(Jwks("kid-1", _rsa)));

            var result = Validate(Token(
                _rsa,
                "kid-1",
                issuer: issuer,
                project: project,
                environment: environment));

            Assert.That(result.UnifiedError.SourceCode, Is.EqualTo(expectedCode));
        }

        [Test]
        public void ExpiredAndTamperedTokens_AreRejected()
        {
            Configure(new FakeJwksTransport(Jwks("kid-1", _rsa)));
            var expired = Validate(Token(_rsa, "kid-1", expiresAt: Now.AddMinutes(-2)));
            var valid = Token(_rsa, "kid-1");
            var tampered = valid.Substring(0, valid.Length - 2) + "aa";
            var invalidSignature = Validate(tampered);

            Assert.That(expired.UnifiedError.SourceCode, Is.EqualTo("jwt_expired"));
            Assert.That(invalidSignature.UnifiedError.SourceCode, Is.EqualTo("jwt_signature_invalid"));
        }

        [Test]
        public void CachedJwks_IsReusedWithinMaxAge()
        {
            var jwks = new FakeJwksTransport(Jwks("kid-1", _rsa));
            Configure(jwks);

            Assert.That(Validate(Token(_rsa, "kid-1")).IsValid, Is.True);
            Assert.That(Validate(Token(_rsa, "kid-1", tokenId: "jti_2")).IsValid, Is.True);

            Assert.That(jwks.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void CanceledValidation_DoesNotFetchJwks()
        {
            var jwks = new FakeJwksTransport(Jwks("kid-1", _rsa));
            Configure(jwks);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                PlayServGameServer.ValidatePlayerTokenAsync(
                        Token(_rsa, "kid-1"),
                        Options(),
                        cancellation.Token)
                    .GetAwaiter().GetResult());
            Assert.That(jwks.CallCount, Is.Zero);
        }

        private static void Configure(FakeJwksTransport jwks)
        {
            PlayServGameServer.ConfigureForTesting(
                new PlayServGameServerOptions
                {
                    BackendServerAddress = "https://api.playserv.test",
                    ServerKeyProvider = new UnusedKeyProvider()
                },
                new UnusedHttpTransport(),
                () => Now,
                (_, __) => Task.CompletedTask);
            PlayServGameServer.ConfigurePlayerTokenValidatorForTesting(jwks, () => Now);
        }

        private static PlayServPlayerTokenValidationResult Validate(string token) =>
            PlayServGameServer.ValidatePlayerTokenAsync(token, Options()).GetAwaiter().GetResult();

        private static PlayServPlayerTokenValidationOptions Options() =>
            new PlayServPlayerTokenValidationOptions
            {
                ExpectedProjectId = "prj_1",
                ExpectedEnvironment = "prod"
            };

        private static string Token(
            RSA rsa,
            string kid,
            string algorithm = "RS256",
            string issuer = "playserv",
            string project = "prj_1",
            string environment = "prod",
            DateTimeOffset? expiresAt = null,
            string tokenId = "jti_1")
        {
            var codec = new NewtonsoftJsonCodec();
            var header = Base64Url(Encoding.UTF8.GetBytes(codec.Serialize(new Dictionary<string, object>
            {
                ["alg"] = algorithm,
                ["kid"] = kid,
                ["typ"] = "JWT"
            })));
            var payload = Base64Url(Encoding.UTF8.GetBytes(codec.Serialize(new Dictionary<string, object>
            {
                ["iss"] = issuer,
                ["sub"] = "plr_1",
                ["project_id"] = project,
                ["env"] = environment,
                ["session_id"] = "psess_1",
                ["providers"] = new[] { "google", "steam" },
                ["iat"] = Now.ToUnixTimeSeconds(),
                ["exp"] = (expiresAt ?? Now.AddMinutes(15)).ToUnixTimeSeconds(),
                ["jti"] = tokenId
            })));
            var input = header + "." + payload;
            var signature = rsa.SignData(
                Encoding.ASCII.GetBytes(input),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return input + "." + Base64Url(signature);
        }

        private static string Jwks(string kid, RSA rsa)
        {
            var parameters = rsa.ExportParameters(false);
            return new NewtonsoftJsonCodec().Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        alg = "RS256",
                        kid,
                        n = Base64Url(parameters.Modulus),
                        e = Base64Url(parameters.Exponent)
                    }
                }
            });
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private sealed class FakeJwksTransport : IPlayServGameServerJwksTransport
        {
            private readonly Queue<string> _responses;
            private string _last;
            internal FakeJwksTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }
            internal int CallCount { get; private set; }
            internal string LastUrl { get; private set; }
            public Task<PlayServJwksResponse> GetAsync(
                string url,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                LastUrl = url;
                if (_responses.Count > 0)
                    _last = _responses.Dequeue();
                return Task.FromResult(new PlayServJwksResponse
                {
                    StatusCode = 200,
                    Body = _last,
                    CacheControl = "public, max-age=3600"
                });
            }
        }

        private sealed class UnusedKeyProvider : IPlayServServerKeyProvider
        {
            public Task<string> GetServerKeyAsync(CancellationToken cancellationToken = default) =>
                throw new AssertionException("JWT validation must not read the server credential.");
        }

        private sealed class UnusedHttpTransport : IPlayServGameServerTransport
        {
            public Task<PlayServGameServerHttpResponse> SendAsync(
                PlayServGameServerHttpRequest request,
                CancellationToken cancellationToken) =>
                throw new AssertionException("JWT validation must use only the credential-free JWKS transport.");
        }
    }
}
