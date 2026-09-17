using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Wrapper;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServSessionVerificationTests
    {
        private H.Http _http;
        private H.Key _key;
        [SetUp] public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown();
            _http = new H.Http(); _key = new H.Key();
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://api.playserv.test", ServerKeyProvider = _key, ExecutorSlug = "arena" },
                _http, () => DateTimeOffset.UtcNow, (d, ct) => Task.Delay(d, ct));
        }
        [TearDown] public void Teardown() => PlayServGameServer.CancelForModuleShutdown();
        private void Respond(string body, int status = 200) => _http.Handler = (r, ct) =>
            Task.FromResult(new PlayServGameServerHttpResponse { StatusCode = status, Body = body });

        [TestCase("expired")][TestCase("invalid")][TestCase("revoked")][TestCase("player_mismatch")]
        [TestCase("player_banned")][TestCase("player_suspended")][TestCase("future_reason")]
        public void VerdictAndExactWire(string reason)
        {
            Respond("{\"valid\":false,\"reason\":\"" + reason + "\"}");
            var result = PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c").GetAwaiter().GetResult();
            Assert.That(result.Valid, Is.False); Assert.That(result.Reason, Is.EqualTo(reason));
            var request = _http.Requests.Single();
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.RelativePath, Is.EqualTo("players/sessions:verify"));
            Assert.That(request.JsonBody, Is.EqualTo("{\"player_id\":\"plr_one\",\"token\":\"a.b.c\"}"));
            Assert.That(request.ServerKey, Is.EqualTo("sk_uplink_test"));
            Assert.That(request.Headers, Is.Null);
        }
        [TestCase("")][TestCase("{}")][TestCase("null")][TestCase("[]")][TestCase("oops a.b.c")]
        [TestCase("{\"valid\":\"true\"}")][TestCase("{\"valid\":null}")][TestCase("{\"valid\":true,\"reason\":1}")]
        public void MalformedIsNotAVerdict(string body)
        {
            Respond(body);
            var error = Assert.Throws<PlayServGameServerException>(() =>
                PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c").GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.InvalidResponse));
            Assert.That(error.ToString(), Does.Not.Contain("a.b.c"));
        }
        [Test] public void ValidationAndPreCancellationDoNotSend()
        {
            foreach (var value in new[] { "", " ", "one\n", "one\t", "one\0" })
            {
                Assert.Throws<ArgumentException>(() => PlayServGameServer.VerifyPlayerSessionAsync(value, "token").GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => PlayServGameServer.VerifyPlayerSessionAsync("plr_one", value).GetAwaiter().GetResult());
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "token", cancel.Token).GetAwaiter().GetResult());
            Assert.That(_http.Requests, Is.Empty);
        }
        [UnityTest] public IEnumerator KeyRotationSessionAndSecretSafety() => H.Run(async () =>
        {
            Respond("{\"valid\":true}");
            Assert.That((await PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c")).Valid, Is.True);
            _key.Value = "sk_rotated";
            await PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c");
            Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("sk_rotated"));
            var socket = new H.Socket(H.Ack()); PlayServGameServer.Uplink.SocketFactory = () => socket;
            await PlayServGameServer.Uplink.ConnectAsync();
            await PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c");
            Assert.That(_http.Requests.Last().ServerKey, Is.EqualTo("session-one"));
            Respond("{\"code\":\"a.b.c\",\"detail\":\"sk_rotated\"}", 403);
            var error = await H.Error(() => PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c"));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Forbidden));
            Assert.That(error.UnifiedError.SourceCode + error.ToString() + error.UnifiedError.RawDetails, Does.Not.Contain("a.b.c").And.Not.Contain("sk_rotated"));
            Respond("{\"valid\":false,\"reason\":\"a.b.c\"}");
            Assert.That((await PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c")).Reason, Is.EqualTo("[REDACTED]"));
        });
        [TestCase(false)][TestCase(true)] public void TransportFailuresAreTyped(bool timeout)
        {
            _http.Handler = (r, ct) => throw new PlayServGameServerTransportFailure("a.b.c", timeout);
            var e = Assert.Throws<PlayServGameServerException>(() => PlayServGameServer.VerifyPlayerSessionAsync("plr_one", "a.b.c").GetAwaiter().GetResult());
            Assert.That(e.UnifiedError.Code, Is.EqualTo(timeout ? PlayServErrorCode.Timeout : PlayServErrorCode.Network));
            Assert.That(e.ToString(), Does.Not.Contain("a.b.c"));
        }
    }
}
