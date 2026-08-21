using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Identity;
using Playserv.Wrapper;

namespace Playserv.SteamAuth.Tests
{
    public sealed class PlayServSteamAuthApiTests
    {
        [Test]
        public void EncodeTicket_UsesOnlyReportedBytesAndLowercaseHex()
        {
            Assert.That(
                PlayServSteamworksNetBridge.EncodeTicket(new byte[] { 0x00, 0xAF, 0x10, 0xFF }, 3),
                Is.EqualTo("00af10"));
        }

        [Test]
        public void Credential_DisposeCancelsTicketExactlyOnce()
        {
            var releases = 0;
            var credential = new PlayServSteamCredential("00af", () => releases++);

            Assert.That(credential.TryCreateBackendProof(out var proof), Is.True);
            Assert.That(proof.ProviderId, Is.EqualTo(PlayServIdentityProviderIds.Steam));
            Assert.That(proof.ProviderToken, Is.EqualTo("00af"));
            Assert.That(proof.Mode, Is.Empty);

            credential.Dispose();
            credential.Dispose();

            Assert.That(releases, Is.EqualTo(1));
            Assert.That(credential.IsReleased, Is.True);
            Assert.That(credential.TryCreateBackendProof(out _), Is.False);
        }

        [Test]
        public void LoginAsync_ForwardsModeAndReleasesTicketAfterAuthCompletes()
        {
            var releases = 0;
            var provider = new TestProvider
            {
                Credential = new PlayServSteamCredential("deadbeef", () => releases++)
            };
            PlayServExternalIdentityProof captured = null;
            var capturedMode = PlayServExternalLoginMode.PreserveCurrentPlayer;
            var api = new PlayServSteamAuthApi(
                provider,
                (proof, mode, _) =>
                {
                    captured = proof;
                    capturedMode = mode;
                    Assert.That(releases, Is.Zero);
                    return Task.FromResult<PlayServAuthResult>(null);
                },
                (_, __) => Task.FromResult<PlayServAuthResult>(null));

            var result = api.LoginAsync(PlayServExternalLoginMode.RecoverProviderAccount)
                .GetAwaiter().GetResult();

            Assert.That(result, Is.Null);
            Assert.That(captured.ProviderId, Is.EqualTo(PlayServIdentityProviderIds.Steam));
            Assert.That(captured.ProviderToken, Is.EqualTo("deadbeef"));
            Assert.That(capturedMode, Is.EqualTo(PlayServExternalLoginMode.RecoverProviderAccount));
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void LinkAsync_ProviderFailureDoesNotInvokePlayServAuth()
        {
            var authCalls = 0;
            var api = new PlayServSteamAuthApi(
                new TestProvider { Failure = new PlayServSteamAuthException("ticket", "provider_rejected", "Rejected.") },
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

            Assert.Throws<PlayServSteamAuthException>(() => api.LinkAsync().GetAwaiter().GetResult());
            Assert.That(authCalls, Is.Zero);
        }

        [Test]
        public void SteamworksBridge_CorrelatesCallbackAndCancelsTicketOnDispose()
        {
            Steamworks.SteamAPI.Running = true;
            Steamworks.SteamUser.LoggedOn = true;
            Steamworks.SteamUser.Reset();
            Assert.That(PlayServSteamworksNetBridge.TryCreate(out var bridge), Is.True);
            Assert.That(bridge.IsInitialized, Is.True);

            var task = bridge.GetCredentialAsync(CancellationToken.None);
            Steamworks.Callback<Steamworks.GetTicketForWebApiResponse_t>.Dispatch(
                new Steamworks.GetTicketForWebApiResponse_t
                {
                    m_hAuthTicket = new Steamworks.HAuthTicket { m_HAuthTicket = 999 },
                    m_eResult = Steamworks.EResult.OK,
                    m_rgubTicket = new byte[] { 0xFF },
                    m_cubTicket = 1
                });
            Assert.That(task.IsCompleted, Is.False);

            Steamworks.Callback<Steamworks.GetTicketForWebApiResponse_t>.Dispatch(
                new Steamworks.GetTicketForWebApiResponse_t
                {
                    m_hAuthTicket = Steamworks.SteamUser.LastIssued,
                    m_eResult = Steamworks.EResult.OK,
                    m_rgubTicket = new byte[] { 0x01, 0xAB, 0xCC },
                    m_cubTicket = 2
                });

            var credential = task.GetAwaiter().GetResult();
            Assert.That(credential.Ticket, Is.EqualTo("01ab"));
            Assert.That(Steamworks.SteamUser.LastIdentity, Is.Null);
            credential.Dispose();
            Assert.That(Steamworks.SteamUser.CancelCount, Is.EqualTo(1));
        }

        [Test]
        public void SteamworksBridge_CancellationReleasesPendingTicketAndIgnoresLateCallback()
        {
            Steamworks.SteamAPI.Running = true;
            Steamworks.SteamUser.LoggedOn = true;
            Steamworks.SteamUser.Reset();
            PlayServSteamworksNetBridge.TryCreate(out var bridge);
            using var cancellation = new CancellationTokenSource();

            var task = bridge.GetCredentialAsync(cancellation.Token);
            var issued = Steamworks.SteamUser.LastIssued;
            cancellation.Cancel();

            Assert.Throws<TaskCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(Steamworks.SteamUser.CancelCount, Is.EqualTo(1));

            Steamworks.Callback<Steamworks.GetTicketForWebApiResponse_t>.Dispatch(
                new Steamworks.GetTicketForWebApiResponse_t
                {
                    m_hAuthTicket = issued,
                    m_eResult = Steamworks.EResult.OK,
                    m_rgubTicket = new byte[] { 0x01 },
                    m_cubTicket = 1
                });
            Assert.That(Steamworks.SteamUser.CancelCount, Is.EqualTo(1));
        }

        private sealed class TestProvider : IPlayServSteamAuthProvider
        {
            public bool IsAvailable => true;
            public bool IsInitialized => true;
            public PlayServSteamCredential Credential { get; set; }
            public PlayServSteamAuthException Failure { get; set; }

            public Task<PlayServSteamCredential> GetCredentialAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (Failure != null)
                    throw Failure;
                return Task.FromResult(Credential);
            }
        }
    }
}

namespace Steamworks
{
    public enum EResult
    {
        OK = 1,
        Fail = 2
    }

    public struct HAuthTicket
    {
        public uint m_HAuthTicket;
    }

    public struct GetTicketForWebApiResponse_t
    {
        public HAuthTicket m_hAuthTicket;
        public EResult m_eResult;
        public byte[] m_rgubTicket;
        public uint m_cubTicket;
    }

    public static class SteamAPI
    {
        public static bool Running;
        public static bool IsSteamRunning() => Running;
    }

    public static class SteamUser
    {
        private static uint _nextHandle;

        public static bool LoggedOn;
        public static HAuthTicket LastIssued { get; private set; }
        public static string LastIdentity { get; private set; }
        public static int CancelCount { get; private set; }

        public static bool BLoggedOn() => LoggedOn;

        public static HAuthTicket GetAuthTicketForWebApi(string identity)
        {
            LastIdentity = identity;
            LastIssued = new HAuthTicket { m_HAuthTicket = ++_nextHandle };
            return LastIssued;
        }

        public static void CancelAuthTicket(HAuthTicket ticket)
        {
            if (ticket.m_HAuthTicket != 0)
                CancelCount++;
        }

        public static void Reset()
        {
            LastIssued = default;
            LastIdentity = "not-called";
            CancelCount = 0;
        }
    }

    public sealed class Callback<T>
    {
        public delegate void DispatchDelegate(T value);

        private static DispatchDelegate _callback;

        public static Callback<T> Create(DispatchDelegate callback)
        {
            _callback = callback;
            return new Callback<T>();
        }

        public static void Dispatch(T value) => _callback?.Invoke(value);
    }
}
