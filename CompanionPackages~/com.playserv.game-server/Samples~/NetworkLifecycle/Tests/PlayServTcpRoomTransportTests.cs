using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;

namespace Playserv.Samples.NetworkLifecycle.Tests
{
    public sealed class PlayServTcpRoomTransportTests
    {
        private PlayServTcpRoomTransport _server;
        private TcpClient _client;
        private PlayServHostedLaunch _launch;

        [SetUp] public void Setup()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var values = new Dictionary<string, string>
            {
                ["PLAYSERV_API_URL"] = "https://api.playserv.test", ["PLAYSERV_EXECUTOR_SLUG"] = "arena",
                ["PLAYSERV_DEPLOYMENT_ID"] = "sample", ["PLAYSERV_ROOM_NAME"] = "one",
                ["PLAYSERV_ROOM_LISTEN_PORT"] = port.ToString(), ["PLAYSERV_PUBLIC_HOST"] = "127.0.0.1",
                ["PLAYSERV_ROOM_ATTRIBUTES"] = "{}", ["PLAYSERV_PORTS_MAPPING"] = null,
                ["PLAYSERV_DEPLOYMENT_TOKEN"] = Encode("{\"alg\":\"HS256\"}") + "." +
                    Encode("{\"sub\":\"executor/arena\",\"deployment_id\":\"sample\"}") + ".signature"
            };
            var previous = new Dictionary<string, string>();
            try
            {
                foreach (var pair in values) { previous[pair.Key] = Environment.GetEnvironmentVariable(pair.Key); Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
                _launch = PlayServServerLaunch.ReadHosted("tcp");
            }
            finally { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
            _server = new PlayServTcpRoomTransport(); _client = new TcpClient();
        }

        [UnityTearDown] public IEnumerator Cleanup() => Run(async () =>
        { _client.Dispose(); await _server.StopAsync(CancellationToken.None); });

        [UnityTest] public IEnumerator RealTcpConnectionWaitsForAdmissionAndClosesWhenRemoved() => Run(async () =>
        {
            IPlayServRoomNetworkConnection pending = null; string ticket = null;
            _server.Authenticating += (connection, token) => { pending = connection; ticket = token; };
            await _server.StartAsync(_launch, CancellationToken.None);
            await _client.ConnectAsync("127.0.0.1", _launch.ListenPort);
            var stream = _client.GetStream(); var hello = Encoding.UTF8.GetBytes("rsv_test\n");
            await stream.WriteAsync(hello, 0, hello.Length);
            await Until(() => pending != null);
            Assert.That(ticket, Is.EqualTo("rsv_test")); Assert.That(_client.Available, Is.Zero);
            pending.Accept("plr_verified");
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
            Assert.That(await Read(reader), Is.EqualTo("ready plr_verified"));
            await stream.WriteAsync(Encoding.UTF8.GetBytes("ping\n"), 0, 5);
            Assert.That(await Read(reader), Is.EqualTo("pong plr_verified"));
            pending.Close("player_removed");
            Assert.That(await Read(reader), Is.Null);
        });

        [UnityTest] public IEnumerator RefusedTcpConnectionNeverReachesGameplay() => Run(async () =>
        {
            _server.Authenticating += (connection, _) => connection.Close("reservation_invalid");
            await _server.StartAsync(_launch, CancellationToken.None);
            await _client.ConnectAsync("127.0.0.1", _launch.ListenPort);
            var stream = _client.GetStream(); var bytes = Encoding.UTF8.GetBytes("rsv_invalid\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            using var reader = new StreamReader(stream);
            Assert.That(await Read(reader), Is.Null);
        });

        [UnityTest] public IEnumerator OversizedHandshakeIsClosedWithoutAuthentication() => Run(async () =>
        {
            var requests = 0; _server.Authenticating += (_, __) => requests++;
            await _server.StartAsync(_launch, CancellationToken.None);
            await _client.ConnectAsync("127.0.0.1", _launch.ListenPort);
            var stream = _client.GetStream(); var bytes = Encoding.UTF8.GetBytes(new string('x', 4097));
            await stream.WriteAsync(bytes, 0, bytes.Length);
            using var reader = new StreamReader(stream);
            Assert.That(await Read(reader), Is.Null); Assert.That(requests, Is.Zero);
        });

        [UnityTest] public IEnumerator StopClosesAConnectionStillWaitingForAuthentication() => Run(async () =>
        {
            var disconnected = 0; _server.Disconnected += _ => disconnected++;
            _server.Authenticating += (_, __) => { };
            await _server.StartAsync(_launch, CancellationToken.None);
            await _client.ConnectAsync("127.0.0.1", _launch.ListenPort);
            var stream = _client.GetStream(); var bytes = Encoding.UTF8.GetBytes("rsv_test\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await Task.Delay(100);
            await _server.StopAsync(CancellationToken.None);
            await _server.StopAsync(CancellationToken.None);
            await Until(() => disconnected == 1);
            using var reader = new StreamReader(stream);
            Assert.That(await Read(reader), Is.Null);
        });

        [UnityTest] public IEnumerator CancelledStopWaitStillClosesTheSocketAndAllowsCleanupToBeAwaited() => Run(async () =>
        {
            IPlayServRoomNetworkConnection pending = null;
            _server.Authenticating += (connection, _) => pending = connection;
            await _server.StartAsync(_launch, CancellationToken.None);
            await _client.ConnectAsync("127.0.0.1", _launch.ListenPort);
            var stream = _client.GetStream(); var bytes = Encoding.UTF8.GetBytes("rsv_test\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await Until(() => pending != null);
            var cancelled = false;
            try { await _server.StopAsync(new CancellationToken(true)); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert.That(cancelled, Is.True, "The caller's cancellation must cancel its wait, not transport cleanup.");
            await _server.StopAsync(CancellationToken.None);
            using var reader = new StreamReader(stream);
            Assert.That(await Read(reader), Is.Null);
        });

        private static async Task<string> Read(StreamReader reader)
        {
            var read = reader.ReadLineAsync();
            Assert.That(await Task.WhenAny(read, Task.Delay(3000)), Is.SameAs(read), "Socket operation timed out.");
            return await read;
        }
        private static async Task Until(Func<bool> predicate)
        {
            for (var i = 0; i < 300; i++) { if (predicate()) return; await Task.Delay(10); }
            Assert.Fail("Expected socket callback was not received.");
        }
        private static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var start = DateTime.UtcNow;
            while (!task.IsCompleted) { Assert.That(DateTime.UtcNow - start, Is.LessThan(TimeSpan.FromSeconds(15))); yield return null; }
            task.GetAwaiter().GetResult();
        }
    }
}
