using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Identity;
using Playserv.Wrapper;

namespace Playserv.EpicAuth.Tests
{
    public sealed class PlayServEpicAuthApiTests
    {
        [Test]
        public void Requests_SelectExpectedCredentialSource()
        {
            var eos = PlayServEpicAuthRequest.Eos(" account-id ");
            var launcher = PlayServEpicAuthRequest.Launcher(" exchange-code ");

            Assert.That(eos.Source, Is.EqualTo(PlayServEpicCredentialSource.EosAccessToken));
            Assert.That(eos.EpicAccountId, Is.EqualTo("account-id"));
            Assert.That(launcher.Source, Is.EqualTo(PlayServEpicCredentialSource.LauncherExchangeCode));
            Assert.That(launcher.ExchangeCode, Is.EqualTo("exchange-code"));
        }

        [TestCase(PlayServEpicCredentialSource.EosAccessToken, "")]
        [TestCase(PlayServEpicCredentialSource.LauncherExchangeCode, PlayServIdentityProviderModes.EpicLauncherExchangeCode)]
        public void Credential_UsesBackendModeForItsSource(
            PlayServEpicCredentialSource source,
            string expectedMode)
        {
            var credential = new PlayServEpicCredential(source, "epic-token");

            Assert.That(credential.TryCreateBackendProof(out var proof), Is.True);
            Assert.That(proof.ProviderId, Is.EqualTo(PlayServIdentityProviderIds.Epic));
            Assert.That(proof.ProviderToken, Is.EqualTo("epic-token"));
            Assert.That(proof.Mode, Is.EqualTo(expectedMode));
        }

        [Test]
        public void LoginAsync_ForwardsRequestModeAndProof()
        {
            var request = PlayServEpicAuthRequest.Launcher("exchange-code");
            var provider = new TestProvider
            {
                Credential = new PlayServEpicCredential(
                    PlayServEpicCredentialSource.LauncherExchangeCode,
                    "exchange-code")
            };
            PlayServExternalIdentityProof captured = null;
            PlayServExternalLoginMode capturedMode = default;
            var api = new PlayServEpicAuthApi(
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
            Assert.That(captured.ProviderToken, Is.EqualTo("exchange-code"));
            Assert.That(captured.Mode, Is.EqualTo(PlayServIdentityProviderModes.EpicLauncherExchangeCode));
            Assert.That(capturedMode, Is.EqualTo(PlayServExternalLoginMode.RecoverProviderAccount));
        }

        [Test]
        public void LinkAsync_ProviderFailureDoesNotInvokePlayServAuth()
        {
            var authCalls = 0;
            var api = new PlayServEpicAuthApi(
                new TestProvider { Failure = new PlayServEpicAuthException("EOS", "provider_rejected", "Rejected.") },
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

            Assert.Throws<PlayServEpicAuthException>(() =>
                api.LinkAsync(PlayServEpicAuthRequest.Eos()).GetAwaiter().GetResult());
            Assert.That(authCalls, Is.Zero);
        }

        [Test]
        public void EosBridge_CopiesLocalAndExplicitEpicAccountTokens()
        {
            PlayEveryWare.EpicOnlineServices.EOSManager.Instance.Reset();
            Assert.That(PlayServEosContribBridge.TryCreate(out var bridge), Is.True);
            Assert.That(bridge.IsInitialized, Is.True);

            var local = bridge.GetCredential(PlayServEpicAuthRequest.Eos());
            var explicitUser = bridge.GetCredential(PlayServEpicAuthRequest.Eos("other-account"));

            Assert.That(local.Token, Is.EqualTo("access-local-account"));
            Assert.That(explicitUser.Token, Is.EqualTo("access-other-account"));
            Assert.That(local.Source, Is.EqualTo(PlayServEpicCredentialSource.EosAccessToken));
        }

        [Test]
        public void EosBridge_UsesLauncherOverrideOrLauncherArguments()
        {
            PlayEveryWare.EpicOnlineServices.EOSManager.LauncherArguments =
                new PlayEveryWare.EpicOnlineServices.EOSManager.EpicLauncherArgs
                {
                    authPassword = "launcher-argument"
                };
            PlayServEosContribBridge.TryCreate(out var bridge);

            var automatic = bridge.GetCredential(PlayServEpicAuthRequest.Launcher());
            var explicitCode = bridge.GetCredential(PlayServEpicAuthRequest.Launcher("override"));

            Assert.That(automatic.Token, Is.EqualTo("launcher-argument"));
            Assert.That(explicitCode.Token, Is.EqualTo("override"));
            Assert.That(explicitCode.Source, Is.EqualTo(PlayServEpicCredentialSource.LauncherExchangeCode));
        }

        private sealed class TestProvider : IPlayServEpicAuthProvider
        {
            public bool IsAvailable => true;
            public bool IsInitialized => true;
            public PlayServEpicCredential Credential { get; set; }
            public PlayServEpicAuthException Failure { get; set; }
            public PlayServEpicAuthRequest LastRequest { get; private set; }

            public Task<PlayServEpicCredential> GetCredentialAsync(
                PlayServEpicAuthRequest request,
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

namespace PlayEveryWare.EpicOnlineServices
{
    public sealed class EOSManager
    {
        private static readonly EOSSingleton Singleton = new EOSSingleton();

        public static EOSSingleton Instance => Singleton;

        public static EpicLauncherArgs LauncherArguments;

        public struct EpicLauncherArgs
        {
            public string authPassword;
        }

        public static EpicLauncherArgs GetCommandLineArgsFromEpicLauncher() => LauncherArguments;

        public sealed class EOSSingleton
        {
            public Epic.OnlineServices.Auth.AuthInterface GetEOSAuthInterface() =>
                new Epic.OnlineServices.Auth.AuthInterface();

            public Epic.OnlineServices.EpicAccountId GetLocalUserId() =>
                Epic.OnlineServices.EpicAccountId.FromString("local-account");

            public void Reset()
            {
                EOSManager.LauncherArguments = default;
            }
        }
    }
}

namespace Epic.OnlineServices
{
    public enum Result
    {
        Success = 0,
        InvalidAuth = 1
    }

    public sealed class EpicAccountId
    {
        private EpicAccountId(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static EpicAccountId FromString(string value) => new EpicAccountId(value);

        public bool IsValid() => !string.IsNullOrWhiteSpace(Value);
    }
}

namespace Epic.OnlineServices.Auth
{
    public struct CopyUserAuthTokenOptions
    {
    }

    public struct Token
    {
        public string AccessToken { get; set; }
    }

    public sealed class AuthInterface
    {
        public Epic.OnlineServices.Result CopyUserAuthToken(
            ref CopyUserAuthTokenOptions options,
            Epic.OnlineServices.EpicAccountId accountId,
            out Token? token)
        {
            token = new Token { AccessToken = "access-" + accountId.Value };
            return Epic.OnlineServices.Result.Success;
        }
    }
}
