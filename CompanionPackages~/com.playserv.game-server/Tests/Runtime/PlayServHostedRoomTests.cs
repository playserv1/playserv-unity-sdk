using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using Playserv.Wrapper;
using UnityEngine.TestTools;
using static Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServHostedRoomTests
    {
        private Dictionary<string, string> _environment;
        private string _fileToken;
        private PlayServGameServerUplinkTests.Http _http;
        private Socket _socket;
        private Listener _listener;

        [SetUp] public void SetUp()
        {
            PlayServGameServer.CancelForModuleShutdown();
            _environment = new Dictionary<string, string>
            {
                ["PLAYSERV_API_URL"] = "https://api.playserv.test",
                ["PLAYSERV_EXECUTOR_SLUG"] = "arena",
                ["PLAYSERV_DEPLOYMENT_ID"] = "dpr_hosted",
                ["PLAYSERV_ROOM_NAME"] = "hosted-one",
                ["PLAYSERV_ROOM_LISTEN_PORT"] = "30123",
                ["PLAYSERV_PUBLIC_HOST"] = "machine.example.test",
                ["PLAYSERV_ROOM_ATTRIBUTES"] = "{\"Map\":\"arena\",\"Mode\":\"duel\"}"
            };
            _fileToken = Token("dpr_hosted");
            _http = new PlayServGameServerUplinkTests.Http(); _socket = new Socket(Ack()); _listener = new Listener();
            PlayServGameServer.Uplink.SocketFactory = () => _socket;
        }

        [UnityTearDown] public IEnumerator TearDown() => Run(async () =>
        {
            try { await PlayServGameServer.ShutdownAsync(); }
            finally
            {
                PlayServGameServer.CancelForModuleShutdown();
                // An uncertain launch deliberately requires a fresh process. Simulate that only between tests.
                typeof(PlayServGameServer).GetField("_hostedRoom", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static).SetValue(null, null);
            }
        });

        private string EnvironmentValue(string name) => _environment.TryGetValue(name, out var value) ? value : null;
        private string ReadFile(string path)
        {
            Assert.That(path, Is.EqualTo("/run/playserv/deployment-token"));
            return _fileToken;
        }
        private PlayServHostedLaunch Launch(string transport = "udp") =>
            PlayServServerLaunch.ReadHosted(transport, EnvironmentValue, ReadFile);
        private Task<PlayServHostedRoom> Start(CancellationToken ct = default, PlayServHostedRoomOptions options = null) =>
            PlayServGameServer.StartHostedRoomAsync(_listener, options ?? new PlayServHostedRoomOptions(), Launch(), _http, ct);

        [UnityTest] public IEnumerator EarlyHostedStopDoesNotHoldItsLockWhileWaitingForConfigurationCleanup() => Run(async () =>
        {
            _socket = new Socket(null);
            var starting = Start();
            await Until(() => _socket.Sent.Any());
            var flags = System.Reflection.BindingFlags.NonPublic;
            var hosted = (PlayServHostedRoom)typeof(PlayServGameServer).GetField("_hostedRoom", flags | System.Reflection.BindingFlags.Static).GetValue(null);
            var configurationGate = typeof(PlayServGameServer).GetField("ConfigurationGate", flags | System.Reflection.BindingFlags.Static).GetValue(null);
            var hostedGate = typeof(PlayServHostedRoom).GetField("_sync", flags | System.Reflection.BindingFlags.Instance).GetValue(hosted);
            using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var owner = Task.Run(() =>
            {
                lock (configurationGate) { held.Set(); release.Wait(TimeSpan.FromSeconds(5)); }
            });
            await Until(() => held.IsSet);
            var stopping = Task.Run(() => hosted.StopWithResultAsync());
            var entered = false;
            try
            {
                entered = await Task.Run(() =>
                {
                    try
                    {
                        if (!SpinWait.SpinUntil(() => typeof(PlayServHostedRoom).GetField("_stopping", flags | System.Reflection.BindingFlags.Instance).GetValue(hosted) != null, 2000))
                            throw new TimeoutException("Hosted stop did not start.");
                        var available = Monitor.TryEnter(hostedGate, TimeSpan.FromMilliseconds(100));
                        if (available) Monitor.Exit(hostedGate);
                        return available;
                    }
                    finally { release.Set(); }
                });
            }
            finally
            {
                release.Set(); await owner; await stopping;
                try { await starting; } catch (OperationCanceledException) { }
            }
            Assert.That(entered, Is.True, "A module shutdown holding ConfigurationGate must still be able to acquire the hosted stop lock.");
        });

        [Test] public void LaunchReadsAssignedIdentityPortAttributesAndFileCredential()
        {
            var launch = Launch();
            Assert.That(launch.DeploymentId, Is.EqualTo("dpr_hosted"));
            Assert.That(launch.RoomName, Is.EqualTo("hosted-one"));
            Assert.That(launch.ListenPort, Is.EqualTo(30123));
            Assert.That(launch.Connect.Host, Is.EqualTo("machine.example.test"));
            var attributes = (IDictionary<string, object>)launch.Attributes;
            Assert.That(attributes["Map"], Is.EqualTo("arena"));
            attributes["Map"] = "mutated";
            Assert.That(((IDictionary<string, object>)launch.Attributes)["Map"], Is.EqualTo("arena"));
        }

        [Test] public void LegacyLaunchKeepsLocalPortSeparateFromMappedPublicPort()
        {
            _environment.Remove("PLAYSERV_PUBLIC_HOST");
            _environment["PLAYSERV_PUBLIC_IP"] = "192.0.2.1";
            _environment["PLAYSERV_PORTS_MAPPING"] = "{\"30123/tcp\":31000}";
            _environment["PLAYSERV_DEPLOYMENT_TOKEN"] = Token("dpr_hosted");
            _fileToken = "invalid-unused-file";
            var launch = Launch("ws");
            Assert.That(launch.ListenPort, Is.EqualTo(30123));
            Assert.That(launch.Connect.Port, Is.EqualTo(31000));
            Assert.That(launch.Connect.Transport, Is.EqualTo("ws"));
        }

        [TestCase("PLAYSERV_ROOM_NAME")]
        [TestCase("PLAYSERV_ROOM_LISTEN_PORT")]
        [TestCase("PLAYSERV_PUBLIC_HOST")]
        [TestCase("PLAYSERV_API_URL")]
        [TestCase("PLAYSERV_EXECUTOR_SLUG")]
        public void MissingLaunchFieldFailsBeforeAnyConnection(string field)
        {
            _environment.Remove(field);
            Assert.Throws<ArgumentException>(() => Launch());
            Assert.That(_socket.Endpoint, Is.Null);
            Assert.That(_http.Requests, Is.Empty);
        }

        [TestCase("PLAYSERV_ROOM_LISTEN_PORT", "65536")]
        [TestCase("PLAYSERV_ROOM_LISTEN_PORT", "0")]
        [TestCase("PLAYSERV_ROOM_ATTRIBUTES", "[]")]
        [TestCase("PLAYSERV_ROOM_ATTRIBUTES", "{not-json")]
        [TestCase("PLAYSERV_DEPLOYMENT_ID", "different-deployment")]
        [TestCase("PLAYSERV_PUBLIC_HOST", "secret@bad/host")]
        public void InvalidLaunchDoesNotEchoItsValue(string field, string value)
        {
            _environment[field] = value;
            var error = Assert.Throws<ArgumentException>(() => Launch());
            if (value.Length > 3) Assert.That(error.ToString(), Does.Not.Contain(value));
        }

        [Test] public void MissingDeploymentCredentialNeverFallsBackToStudioKey()
        {
            _fileToken = null; _environment["PLAYSERV_SERVER_KEY"] = "sk_do_not_use";
            var error = Assert.Throws<ArgumentException>(() => Launch());
            Assert.That(error.Message, Does.Contain("credential"));
            Assert.That(error.ToString(), Does.Not.Contain("sk_do_not_use"));
        }

        [Test] public void MappingSelectsTheListenersProtocolWhenBothPortsArePublished()
        {
            _environment["PLAYSERV_PORTS_MAPPING"] = "[{\"internal_port\":30123,\"external_port\":9000,\"protocol\":\"udp\"},{\"internal_port\":30123,\"external_port\":31000,\"protocol\":\"tcp\"}]";
            Assert.That(Launch("ws").Connect.Port, Is.EqualTo(31000));
        }

        [Test] public void PlatformTlsMappingAdvertisesWssAndKeepsUdpSeparate()
        {
            _environment["PLAYSERV_PORTS_MAPPING"] = "[{\"internal_port\":30123,\"external_port\":31000,\"protocol\":\"wss\"},{\"internal_port\":30123,\"external_port\":30123,\"protocol\":\"udp\"}]";
            Assert.That(Launch("wss").Connect.Port, Is.EqualTo(31000));
            Assert.That(Launch("wss").Connect.Transport, Is.EqualTo("wss"));
            Assert.That(Launch().Connect.Port, Is.EqualTo(30123));
            Assert.That(Launch().Connect.Transport, Is.EqualTo("udp"));
        }

        [UnityTest] public IEnumerator StartupDeadlineDoesNotWaitForAnUncooperativeRegistration() => Run(async () =>
        {
            var response = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, ct) => response.Task;
            var starting = Start(options: new PlayServHostedRoomOptions { StartupTimeout = TimeSpan.FromMilliseconds(200) });
            await Until(() => _http.Requests.Count > 0);
            try
            {
                var completed = await Task.WhenAny(starting, Task.Delay(1500));
                Assert.That(completed, Is.SameAs(starting), "Startup must respect its deadline even when a transport ignores cancellation.");
                var failure = await Failure(starting);
                Assert.That(((PlayServGameServerException)failure).UnifiedError.SourceCode, Is.EqualTo("room_registration_outcome_unknown"));
            }
            finally
            {
                response.TrySetResult(new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{\"created\":true,\"placement\":{\"state\":\"active\",\"open\":true}}" });
                await Failure(starting);
            }
            Assert.That(_listener.Stops, Is.EqualTo(1));
            await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
        });

        [UnityTest] public IEnumerator CredentialRotationCannotChangeTheLaunchIdentity() => Run(async () =>
        {
            var launch = Launch();
            Assert.That(await launch.Credential.GetCredentialAsync(), Is.EqualTo(_fileToken));
            _fileToken = Token("another-deployment");
            var error = await ExpectFailureAsync<ArgumentException>(async () => await launch.Credential.GetCredentialAsync());
            Assert.That(error.ToString(), Does.Not.Contain(_fileToken));
        });

        [Test] public void PlayerBuildRejectsHostedBootstrapBeforeReadingLaunchCredentials()
        {
            PlayServGameServer.SetSupportedBuildForTesting(false);
            try { Assert.Throws<PlatformNotSupportedException>(() => PlayServGameServer.StartHostedRoomAsync(_listener)); }
            finally { PlayServGameServer.SetSupportedBuildForTesting(null); }
            Assert.That(_socket.Endpoint, Is.Null);
            Assert.That(_listener.Starts, Is.Zero);
        }

        [Test] public void PlayerBuildRejectsReadingHostedCredentials()
        {
            PlayServGameServer.SetSupportedBuildForTesting(false);
            try { Assert.Throws<PlatformNotSupportedException>(() => PlayServServerLaunch.ReadHosted()); }
            finally { PlayServGameServer.SetSupportedBuildForTesting(null); }
        }

        [UnityTest] public IEnumerator HostedStartupWaitsForListenerThenRegistersUsingSessionBearer() => Run(async () =>
        {
            var ready = new TaskCompletionSource<bool>();
            var thread = Thread.CurrentThread.ManagedThreadId;
            _listener.OnStart = async (_, ct) => { Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(thread)); await ready.Task; };
            _listener.OnStop = ct => { Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(thread)); return Task.CompletedTask; };
            var starting = Start();
            await Until(() => _listener.Starts == 1);
            Assert.That(starting.IsCompleted, Is.False);
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(_socket.Credential, Is.EqualTo(_fileToken));
            Assert.That(_socket.Sent.First(), Does.Contain("\"instance_id\":\"dpr_hosted\""));
            Assert.That(_socket.Sent.First(), Does.Not.Contain(_fileToken));
            ready.SetResult(true);
            var hosted = await starting;
            var request = _http.Requests.First();
            Assert.That(request.ServerKey, Is.EqualTo("session-one"));
            Assert.That(request.JsonBody, Does.Contain("hosted-one").And.Contain("machine.example.test").And.Contain("30123").And.Contain("\"Map\":\"arena\""));
            Assert.That(hosted.Room.State, Is.EqualTo(PlayServGameRoomState.Active));
            await hosted.StopAsync(); await hosted.StopAsync();
            Assert.That(_listener.Stops, Is.EqualTo(1));
            Assert.That(PlayServGameServer.IsConfigured, Is.False);
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Disconnected));
        });

        [UnityTest] public IEnumerator ConcurrentStartupDoesNotStartAnotherListenerOrRoom() => Run(async () =>
        {
            var ready = new TaskCompletionSource<bool>();
            _listener.OnStart = (_, ct) => ready.Task;
            var first = Start(); await Until(() => _listener.Starts == 1);
            await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
            ready.SetResult(true); var hosted = await first;
            await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
            Assert.That(_listener.Starts, Is.EqualTo(1));
            Assert.That(_http.Requests.Count(r => r.RelativePath.EndsWith("upsert")), Is.EqualTo(1));
            await hosted.StopAsync();
        });

        [UnityTest] public IEnumerator ListenerFailureCleansUpAndHidesGameExceptionSecrets() => Run(async () =>
        {
            _listener.OnStart = (_, ct) => throw new Exception("private-game-secret");
            var error = await Failure(Start());
            Assert.That(error.ToString(), Does.Not.Contain("private-game-secret"));
            Assert.That(_listener.Stops, Is.EqualTo(1));
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(PlayServGameServer.IsConfigured, Is.False);
        });

        [UnityTest] public IEnumerator CancellationBeforeHelloStopsOwnedReconnectLoop() => Run(async () =>
        {
            _socket = new Socket(null);
            using var cancel = new CancellationTokenSource();
            var starting = Start(cancel.Token);
            await Until(() => _socket.Endpoint != null); cancel.Cancel();
            Assert.That(await Failure(starting), Is.InstanceOf<OperationCanceledException>());
            Assert.That(_listener.Starts, Is.Zero);
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Disconnected));
            Assert.That(PlayServGameServer.IsConfigured, Is.False);
        });

        [UnityTest] public IEnumerator LateListenerCompletionIsStoppedAfterCancellation() => Run(async () =>
        {
            var ready = new TaskCompletionSource<bool>();
            _listener.OnStart = (_, ct) => ready.Task;
            using var cancel = new CancellationTokenSource();
            var starting = Start(cancel.Token, new PlayServHostedRoomOptions { CleanupTimeout = TimeSpan.FromMilliseconds(100) });
            await Until(() => _listener.Starts == 1); cancel.Cancel();
            await Failure(starting);
            Assert.That(_listener.Stops, Is.GreaterThanOrEqualTo(1));
            await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
            ready.SetResult(true);
            await Until(() => _listener.Stops == 2);
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator StartupTimeoutUsesIndependentCleanupBudget() => Run(async () =>
        {
            _listener.OnStart = (_, ct) => Task.Delay(Timeout.Infinite, ct);
            _listener.OnStop = ct => { Assert.That(ct.IsCancellationRequested, Is.False); return Task.CompletedTask; };
            await Failure(Start(options: new PlayServHostedRoomOptions { StartupTimeout = TimeSpan.FromMilliseconds(200) }));
            Assert.That(_listener.Stops, Is.EqualTo(1));
            Assert.That(PlayServGameServer.IsConfigured, Is.False);
        });

        [UnityTest] public IEnumerator LateStartCannotReleaseOwnershipWhileAnEarlierStopIsUnsettled() => Run(async () =>
        {
            var ready = new TaskCompletionSource<bool>();
            var firstStop = new TaskCompletionSource<bool>();
            _listener.OnStart = (_, ct) => ready.Task;
            _listener.OnStop = ct => _listener.Stops == 1 ? firstStop.Task : Task.CompletedTask;
            using var cancel = new CancellationTokenSource();
            var starting = Start(cancel.Token, new PlayServHostedRoomOptions { CleanupTimeout = TimeSpan.FromMilliseconds(100) });
            try
            {
                await Until(() => _listener.Starts == 1);
                cancel.Cancel(); await Failure(starting);
                ready.SetResult(true);
                await Until(() => _listener.Stops == 2);
                await Task.Delay(50);
                Assert.Throws<InvalidOperationException>(() => PlayServGameServer.Configure(new PlayServGameServerOptions
                    { BackendServerAddress = "https://manual.test" }));
            }
            finally
            {
                ready.TrySetResult(true); firstStop.TrySetResult(true);
                cancel.Cancel(); await Failure(starting);
                await Task.Delay(50);
            }
        });

        [UnityTest] public IEnumerator ModuleShutdownKeepsOwnershipUntilListenerCleanupFinishes() => Run(async () =>
        {
            var stopped = new TaskCompletionSource<bool>();
            _listener.OnStop = ct => stopped.Task;
            var hosted = await Start();
            try
            {
                new PlayServGameServerModule().Shutdown();
                await Until(() => _listener.Stops == 1);
                Assert.Throws<InvalidOperationException>(() => PlayServGameServer.Configure(new PlayServGameServerOptions
                    { BackendServerAddress = "https://manual.test" }));
                await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
            }
            finally { stopped.TrySetResult(true); await hosted.StopAsync(); }
            PlayServGameServer.Uplink.SocketFactory = () => new Socket(Ack());
            await Start();
        });

        [UnityTest] public IEnumerator ShutdownDuringRegistrationRejectsLateSuccessAndKeepsLaunchReserved() => Run(async () =>
        {
            var response = new TaskCompletionSource<PlayServGameServerHttpResponse>();
            _http.Handler = (_, ct) => response.Task;
            var starting = Start();
            await Until(() => _http.Requests.Count > 0);
            try
            {
                await PlayServGameServer.ShutdownAsync();
                var error = await Failure(starting);
                Assert.That(((PlayServGameServerException)error).UnifiedError.SourceCode, Is.EqualTo("room_registration_outcome_unknown"));
            }
            finally
            {
                response.TrySetResult(new PlayServGameServerHttpResponse { StatusCode = 200, Body = "{\"created\":true,\"placement\":{\"state\":\"active\",\"open\":true}}" });
                await Failure(starting); await Task.Delay(50);
            }
            Assert.That(_listener.Stops, Is.EqualTo(1));
            Assert.That(PlayServGameServer.IsConfigured, Is.False);
            await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
        });

        [UnityTest] public IEnumerator LostRegistrationResponseBlocksRetryAndDoesNotReportSuccess() => Run(async () =>
        {
            _http.Handler = (_, ct) => throw new TimeoutException();
            var error = await Failure(Start());
            Assert.That(error, Is.InstanceOf<PlayServGameServerException>());
            Assert.That(((PlayServGameServerException)error).UnifiedError.SourceCode, Is.EqualTo("room_registration_outcome_unknown"));
            Assert.That(_listener.Stops, Is.EqualTo(1));
            await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
            Assert.That(_http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator GlobalShutdownAndRoomCloseStopTheHostedListener() => Run(async () =>
        {
            var hosted = await Start();
            await hosted.Room.CloseAsync();
            await Until(() => _listener.Stops == 1 && !PlayServGameServer.IsConfigured);
            PlayServGameServer.Uplink.SocketFactory = () => new Socket(Ack());
            await Start();
            await PlayServGameServer.ShutdownAsync();
            Assert.That(_listener.Stops, Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator ExistingManualConfigurationIsNotReplacedOrStopped() => Run(async () =>
        {
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://manual.test", EnableUplink = false, ServerKeyProvider = new Key() },
                _http, () => DateTimeOffset.UtcNow, Task.Delay);
            var room = await PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest("arena", new PlayServGameRoomSnapshot("manual", 0, 8)));
            await ExpectFailureAsync<InvalidOperationException>(async () => await Start());
            Assert.That(room.State, Is.EqualTo(PlayServGameRoomState.Active));
            Assert.That(_listener.Starts, Is.Zero);
            Assert.That(PlayServGameServer.IsConfigured, Is.True);
        });

        private static async Task<TException> ExpectFailureAsync<TException>(Func<Task> operation)
            where TException : Exception
        {
            try { await operation(); }
            catch (Exception error)
            {
                Assert.That(error, Is.TypeOf<TException>());
                return (TException)error;
            }
            Assert.Fail("Expected " + typeof(TException).Name + "."); return null;
        }
        private static async Task<Exception> Failure(Task task)
        {
            try { await task; }
            catch (Exception error) { return error; }
            Assert.Fail("Expected startup failure."); return null;
        }
        private static string Token(string deployment)
        {
            string Part(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return Part("{\"alg\":\"HS256\"}") + "." + Part("{\"sub\":\"executor/arena\",\"deployment_id\":\"" + deployment + "\"}") + ".signature";
        }
        private sealed class Listener : IPlayServHostedRoomTransport
        {
            internal int Starts, Stops;
            internal Func<PlayServHostedLaunch, CancellationToken, Task> OnStart;
            internal Func<CancellationToken, Task> OnStop;
            public Task StartAsync(PlayServHostedLaunch launch, CancellationToken ct)
            { Starts++; return OnStart?.Invoke(launch, ct) ?? Task.CompletedTask; }
            public Task StopAsync(CancellationToken ct)
            { Stops++; return OnStop?.Invoke(ct) ?? Task.CompletedTask; }
        }
    }
}
