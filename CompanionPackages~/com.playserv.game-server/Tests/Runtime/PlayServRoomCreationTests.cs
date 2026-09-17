using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;
using static Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServRoomCreationTests
    {
        private PlayServGameServerUplinkTests.Http _http;
        private Socket _socket;
        private ConcurrentQueue<PlayServRoomCreationOutcome> _outcomes;
        private AdmissionTestScope _scope;
        [SetUp] public void Setup()
        {
            PlayServGameServer.CancelForModuleShutdown();
            _scope = new AdmissionTestScope();
            _outcomes = new ConcurrentQueue<PlayServRoomCreationOutcome>();
            _http = new PlayServGameServerUplinkTests.Http(); _socket = new Socket(Ack());
        }
        [UnityTearDown] public IEnumerator Teardown() => Run(() => _scope.CleanupAsync());
        private void Configure(PlayServRoomFactory factory, int timeoutMs = 5000)
        {
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", InstanceId = "unity-create",
                ServerKeyProvider = new Key(), RoomFactory = factory, RoomCreateTimeout = TimeSpan.FromMilliseconds(timeoutMs)
            }, _http, () => DateTimeOffset.UtcNow, (_, ct) => Task.Delay(Timeout.Infinite, ct));
            PlayServGameServer.Uplink.SocketFactory = () => _socket;
            _scope.Track(PlayServGameServer.Uplink);
            var outcomes = _outcomes;
            Action<PlayServRoomCreationOutcome> completed = outcomes.Enqueue;
            var uplink = PlayServGameServer.Uplink;
            uplink.RoomCreationCompleted += completed;
            _scope.UnsubscribeOnCleanup(() => uplink.RoomCreationCompleted -= completed);
        }
        private static PlayServRoomCreateDecision Prepared(string name) => PlayServRoomCreateDecision.Accept(
            new PlayServGameRoomSnapshot(name, 0, 99, connect: new PlayServGameRoomConnect("game.test", 7777, "udp")));
        private void Request(string name) => _socket.Push("{\"type\":\"room_create\",\"room_name\":\"" + name + "\"}");

        [UnityTest] public IEnumerator CapabilityFactoryAndExactResultRegisterOneRoom() => Run(async () =>
        {
            var calls = 0;
            Configure((request, ct) =>
            {
                calls++; Assert.That(request.Configuration.Capacity, Is.EqualTo(8));
                Assert.That(_http.Requests, Is.Empty);
                return Task.FromResult(Prepared(request.RoomName));
            });
            await PlayServGameServer.Uplink.ConnectAsync();
            Assert.That(_socket.Sent.First(), Does.Contain("\"capabilities\":[\"admission_push\",\"room_create\"]"));
            Request("requested"); await Until(() => _outcomes.Count == 1);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_socket.Sent.Last(), Is.EqualTo("{\"type\":\"room_create_result\",\"room_name\":\"requested\",\"ok\":true}"));
            Assert.That(_http.Requests.Count, Is.EqualTo(1));
            Assert.That(_http.Requests.Single().RelativePath, Is.EqualTo("rooms/arena:upsert"));
            Assert.That(_http.Requests.Single().ServerKey, Is.EqualTo("session-one"));
            Assert.That(_http.Requests.Single().JsonBody, Does.Contain("\"capacity\":8").And.Contain("\"instance_id\":\"unity-create\"").And.Contain("game.test"));
            Assert.That(_outcomes.Single().IsSuccess, Is.True);
            Assert.That(_outcomes.Single().FactoryInvoked, Is.True);
        });

        [UnityTest] public IEnumerator PendingAndActiveDuplicatesDoNotCreateAgain() => Run(async () =>
        {
            var factory = new TaskCompletionSource<PlayServRoomCreateDecision>(); var calls = 0;
            Configure((_, ct) => { calls++; return factory.Task; });
            await PlayServGameServer.Uplink.ConnectAsync();
            Request("same"); await Until(() => calls == 1); Request("same");
            await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_name_conflict"));
            Assert.That(_outcomes.Single().FactoryInvoked, Is.False);
            try { await Start("same"); Assert.Fail(); } catch (InvalidOperationException) { }
            factory.SetResult(Prepared("same")); await Until(() => _outcomes.Count == 2);
            Request("same"); await Until(() => _outcomes.Count == 3);
            Assert.That(calls, Is.EqualTo(1)); Assert.That(_http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator DrainingAndFactoryRefusalUseOnlyContractReasons() => Run(async () =>
        {
            var calls = 0;
            Configure((_, ct) => { calls++; return Task.FromResult(PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.InstanceDraining, "not accepting")); });
            await PlayServGameServer.Uplink.ConnectAsync();
            PlayServGameServer.Uplink.AcceptingRoomRequests = false;
            Request("one"); await Until(() => _outcomes.Count == 1);
            Assert.That(calls, Is.Zero); Assert.That(_socket.Sent.Last(), Does.Contain("\"reason\":\"instance_draining\""));
            PlayServGameServer.Uplink.AcceptingRoomRequests = true;
            Request("two"); await Until(() => _outcomes.Count == 2);
            Assert.That(calls, Is.EqualTo(1)); Assert.That(_socket.Sent.Last(), Does.Contain("\"detail\":\"not accepting\""));
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator SlowFactoryDoesNotBlockPingAndNeverSendsLateSuccess() => Run(async () =>
        {
            var factory = new TaskCompletionSource<PlayServRoomCreateDecision>(); var started = false;
            Configure((_, ct) => { started = true; return factory.Task; }, 100);
            await PlayServGameServer.Uplink.ConnectAsync();
            Request("late"); await Until(() => started);
            _socket.Push("{\"type\":\"ping\"}");
            await Until(() => _socket.Sent.Contains("{\"type\":\"pong\"}"));
            await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_create_timeout"));
            factory.SetResult(Prepared("late")); await Task.Delay(50);
            Assert.That(_socket.Sent.Any(s => s.Contains("room_create_result")), Is.False);
            Assert.That(_http.Requests, Is.Empty);
            await Start("late"); // the canceled attempt released the local reservation
        });

        [UnityTest] public IEnumerator ShutdownCancelsPreparationEvenWhenGameIgnoresCancellation() => Run(async () =>
        {
            var factory = new TaskCompletionSource<PlayServRoomCreateDecision>(); var started = false;
            Configure((_, ct) => { started = true; return factory.Task; });
            await PlayServGameServer.Uplink.ConnectAsync(); Request("late"); await Until(() => started);
            await PlayServGameServer.ShutdownAsync();
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_create_canceled"));
            factory.SetResult(Prepared("late")); await Task.Delay(30);
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(PlayServGameServer.Uplink.State, Is.EqualTo(PlayServUplinkState.Disconnected));
        });

        [UnityTest] public IEnumerator FactoryErrorsReturnContractFailureWithoutLeakingDetails() => Run(async () =>
        {
            Configure((_, ct) => throw new Exception("Bearer sk_factory_secret a.b.c"));
            var errors = new ConcurrentQueue<string>();
            PlayServGameServer.Uplink.OnError += e => errors.Enqueue(e.SourceCode + e.Message + e.RawDetails);
            await PlayServGameServer.Uplink.ConnectAsync(); Request("failed"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_factory_failed"));
            Assert.That(errors.All(s => !s.Contains("sk_") && !s.Contains("a.b.c")), Is.True);
            Assert.That(_socket.Sent.Single(s => s.Contains("room_create_result")), Is.EqualTo(
                "{\"type\":\"room_create_result\",\"room_name\":\"failed\",\"ok\":false,\"reason\":\"room_create_failed\",\"detail\":\"Exception\"}"));
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator ActiveQuotaRefusesTemporarilyAndAllowsCreationAfterClose() => Run(async () =>
        {
            var calls = 0;
            Configure((request, ct) => { calls++; return Task.FromResult(Prepared(request.RoomName)); });
            var first = await Start("first"); await Start("second");
            Request("third"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_quota_exceeded"));
            Assert.That(_outcomes.Single().FactoryInvoked, Is.False); Assert.That(calls, Is.Zero);
            Assert.That(_socket.Sent.Last(), Is.EqualTo("{\"type\":\"room_create_result\",\"room_name\":\"third\",\"ok\":false,\"reason\":\"room_quota_exceeded\"}"));
            Assert.That(PlayServGameServer.Uplink.AcceptingRoomRequests, Is.True);
            await first.CloseAsync();
            Request("third"); await Until(() => _outcomes.Count == 2);
            Assert.That(calls, Is.EqualTo(1)); Assert.That(_outcomes.Last().IsSuccess, Is.True);
        });

        [UnityTest] public IEnumerator PendingRoomsCountTowardsQuotaAndDuplicateNameKeepsPrecedence() => Run(async () =>
        {
            var pending = new TaskCompletionSource<PlayServRoomCreateDecision>(); var calls = 0;
            Configure(async (request, ct) => { Interlocked.Increment(ref calls); await pending.Task; return Prepared(request.RoomName); });
            try
            {
                await PlayServGameServer.Uplink.ConnectAsync();
                Request("one"); await Until(() => calls == 1); Request("two"); await Until(() => calls == 2);
                Request("three"); await Until(() => _outcomes.Count == 1);
                Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_quota_exceeded"));
                Request("one"); await Until(() => _outcomes.Count == 2);
                Assert.That(_outcomes.Last().Error.SourceCode, Is.EqualTo("room_name_conflict"));
                Assert.That(calls, Is.EqualTo(2)); Assert.That(_http.Requests, Is.Empty);
            }
            finally { pending.TrySetResult(null); }
        });

        [UnityTest] public IEnumerator AsyncFactoryFailureReleasesNameAndDoesNotDrainInstance() => Run(async () =>
        {
            var calls = 0;
            Configure(async (request, ct) =>
            {
                if (++calls == 1) { await Task.Yield(); throw new InvalidOperationException("Bearer sk_async_secret a.b.c", new Exception("private inner")); }
                return Prepared(request.RoomName);
            });
            await PlayServGameServer.Uplink.ConnectAsync(); Request("retryable"); await Until(() => _outcomes.Count == 1);
            var refusal = _socket.Sent.Single(s => s.Contains("room_create_result"));
            Assert.That(refusal, Is.EqualTo("{\"type\":\"room_create_result\",\"room_name\":\"retryable\",\"ok\":false,\"reason\":\"room_create_failed\",\"detail\":\"InvalidOperationException\"}"));
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_factory_failed"));
            Assert.That(_http.Requests, Is.Empty); Assert.That(PlayServGameServer.Uplink.AcceptingRoomRequests, Is.True);
            Request("retryable"); await Until(() => _outcomes.Count == 2);
            Assert.That(calls, Is.EqualTo(2)); Assert.That(_outcomes.Last().IsSuccess, Is.True);
            Assert.That(_socket.Sent.Count(s => s.Contains("room_create_result")), Is.EqualTo(2));
        });

        [UnityTest] public IEnumerator IndependentFactoryCancellationIsAFactoryFailure() => Run(async () =>
        {
            Configure((_, ct) => Task.FromCanceled<PlayServRoomCreateDecision>(new CancellationToken(true)));
            await PlayServGameServer.Uplink.ConnectAsync(); Request("canceled-by-game"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_factory_failed"));
            Assert.That(_socket.Sent.Single(s => s.Contains("room_create_result")), Does.Contain("\"reason\":\"room_create_failed\"").And.Contain("\"detail\":\"TaskCanceledException\""));
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator SynchronousFactoryCancellationKeepsItsSafeExceptionType() => Run(async () =>
        {
            Configure((_, ct) => throw new OperationCanceledException("sk_private_cancel a.b.c"));
            await PlayServGameServer.Uplink.ConnectAsync(); Request("sync-cancel"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_factory_failed"));
            Assert.That(_socket.Sent.Single(s => s.Contains("room_create_result")), Is.EqualTo(
                "{\"type\":\"room_create_result\",\"room_name\":\"sync-cancel\",\"ok\":false,\"reason\":\"room_create_failed\",\"detail\":\"OperationCanceledException\"}"));
            Assert.That(_http.Requests, Is.Empty);
        });

        // An unusually long studio-defined exception name must still fit the protocol's detail limit.
        private sealed class RoomFactoryExceptionWithAnIntentionallyLongTypeNameToVerifyTheProtocolDetailLimitWithoutExposingExceptionMessagesStackTracesOrInnerExceptionData : Exception { }

        [UnityTest] public IEnumerator FactoryExceptionDetailIsTheShortTypeNameBoundedTo128Characters() => Run(async () =>
        {
            var failure = new RoomFactoryExceptionWithAnIntentionallyLongTypeNameToVerifyTheProtocolDetailLimitWithoutExposingExceptionMessagesStackTracesOrInnerExceptionData();
            Configure((_, ct) => throw failure);
            await PlayServGameServer.Uplink.ConnectAsync(); Request("bounded"); await Until(() => _outcomes.Count == 1);
            Assert.That(failure.GetType().Name.Length, Is.GreaterThan(128));
            Assert.That(_socket.Sent.Single(s => s.Contains("room_create_result")), Is.EqualTo(
                "{\"type\":\"room_create_result\",\"room_name\":\"bounded\",\"ok\":false,\"reason\":\"room_create_failed\",\"detail\":\"" + failure.GetType().Name.Substring(0, 128) + "\"}"));
        });

        [UnityTest] public IEnumerator FactorySdkExceptionIsNotConfusedWithRegistrationFailure() => Run(async () =>
        {
            Configure((_, ct) => throw UplinkErrors.Exception("room_factory_invalid_result"));
            await PlayServGameServer.Uplink.ConnectAsync(); Request("factory-sdk-error"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_factory_failed"));
            Assert.That(_socket.Sent.Single(s => s.Contains("room_create_result")), Does.Contain("\"reason\":\"room_create_failed\"").And.Contain("\"detail\":\"PlayServGameServerException\""));
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator FactoryFaultAfterSdkTimeoutDoesNotSendLateRefusal() => Run(async () =>
        {
            var factory = new TaskCompletionSource<PlayServRoomCreateDecision>(); var started = false;
            Configure((_, ct) => { started = true; return factory.Task; }, 100);
            await PlayServGameServer.Uplink.ConnectAsync(); Request("timeout-fault"); await Until(() => started);
            await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_create_timeout"));
            await PlayServGameServer.Uplink.DrainRoomCreationAsync();
            factory.SetException(new Exception("sk_late_factory_secret"));
            await Start("timeout-fault"); // the completed request cannot reply or retain this name
            Assert.That(_socket.Sent.Any(s => s.Contains("room_create_result")), Is.False);
            Assert.That(_outcomes.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator FactoryFaultAfterDisconnectDoesNotSendLateRefusal() => Run(async () =>
        {
            var factory = new TaskCompletionSource<PlayServRoomCreateDecision>(); var started = false;
            Configure((_, ct) => { started = true; return factory.Task; });
            await PlayServGameServer.Uplink.ConnectAsync(); Request("disconnect-fault"); await Until(() => started);
            await PlayServGameServer.Uplink.DisconnectAsync();
            await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_create_canceled"));
            factory.SetException(new Exception("sk_late_disconnect_secret"));
            Assert.That(_socket.Sent.Any(s => s.Contains("room_create_result")), Is.False);
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator SdkCancellationWhileFactoryThrowsDoesNotSendRefusal() => Run(async () =>
        {
            var started = false;
            Configure(async (_, ct) =>
            {
                started = true;
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { throw new Exception("sk_shutdown_secret"); }
            });
            await PlayServGameServer.Uplink.ConnectAsync(); Request("shutdown-fault"); await Until(() => started);
            await PlayServGameServer.ShutdownAsync(); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_create_canceled"));
            Assert.That(_socket.Sent.Any(s => s.Contains("room_create_result")), Is.False);
            Assert.That(_http.Requests, Is.Empty);
        });

        [Test] public void StaleConfigurationOrShutdownTakesPrecedenceOverQuota()
        {
            Configure((request, ct) => Task.FromResult(Prepared(request.RoomName)));
            var context = PlayServGameServer.GetContextForServices();
            context.ShuttingDown = true;
            Assert.That(PlayServGameServer.TryReserveRequestedRoom(context, "arena", "new", 0, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("instance_draining"));
            context.ShuttingDown = false;
            Configure((request, ct) => Task.FromResult(Prepared(request.RoomName)));
            Assert.That(PlayServGameServer.TryReserveRequestedRoom(context, "arena", "new", 0, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("instance_draining"));
        }

        [TestCase(0, "room_name_conflict")]
        [TestCase(1, "instance_draining")]
        [TestCase(2, "room_quota_exceeded")]
        [TestCase(3, "room_create_failed")]
        public void RefusalEnumPreservesExistingValuesAndMapsAllContractReasons(int value, string wire)
        {
            Assert.That((int)PlayServRoomCreateRefusal.RoomNameConflict, Is.EqualTo(0));
            Assert.That((int)PlayServRoomCreateRefusal.InstanceDraining, Is.EqualTo(1));
            Assert.That(PlayServRoomCreateDecision.Refuse((PlayServRoomCreateRefusal)value, "safe").Reason, Is.EqualTo(wire));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayServRoomCreateDecision.Refuse((PlayServRoomCreateRefusal)int.MaxValue));
        }

        [UnityTest] public IEnumerator NoFactoryMeansNoCapabilityOrDispatch() => Run(async () =>
        {
            Configure(null); await PlayServGameServer.Uplink.ConnectAsync();
            Request("ignored"); await Until(() => PlayServGameServer.Uplink.LastError.SourceCode == "room_create_not_enabled");
            Assert.That(_socket.Sent.Single(), Does.Contain("\"capabilities\":[\"admission_push\"]"));
            Assert.That(_http.Requests, Is.Empty);
        });

        [UnityTest] public IEnumerator FactoryMustPreserveNameAndSupplyConnect() => Run(async () =>
        {
            Configure((_, ct) => Task.FromResult(Prepared("different")));
            await PlayServGameServer.Uplink.ConnectAsync(); Request("original"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_factory_invalid_result"));
            Assert.That(_http.Requests, Is.Empty);
            Assert.That(_socket.Sent.Any(s => s.Contains("room_create_result")), Is.False);
        });

        [UnityTest] public IEnumerator FactoryAndCompletionAreDeliveredThroughConfiguredContext() => Run(async () =>
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var deliveries = new ConcurrentQueue<Action>(); var invoked = false;
            Configure((request, ct) =>
            {
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread)); invoked = true;
                return Task.FromResult(Prepared(request.RoomName));
            });
            PlayServGameServer.GetContextForServices().Dispatch = a => deliveries.Enqueue(a);
            PlayServGameServer.Uplink.RoomCreationCompleted += _ => Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
            await PlayServGameServer.Uplink.ConnectAsync(); Request("main-thread");
            await Until(() =>
            {
                while (deliveries.TryDequeue(out var action)) action();
                return _outcomes.Count == 1;
            });
            Assert.That(invoked, Is.True);
        });

        [UnityTest] public IEnumerator LostRegistrationResponseIsNotRetriedOrCreatedAgain() => Run(async () =>
        {
            var calls = 0;
            Configure((request, ct) => { calls++; return Task.FromResult(Prepared(request.RoomName)); }, 150);
            _http.Handler = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; };
            await PlayServGameServer.Uplink.ConnectAsync(); Request("uncertain"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_registration_outcome_unknown"));
            Assert.That(_socket.Sent.Last(), Does.Contain("\"ok\":true"));
            Assert.That(_http.Requests.Count, Is.EqualTo(1));
            Request("uncertain"); await Until(() => _outcomes.Count == 2);
            Assert.That(_socket.Sent.Last(), Does.Contain("\"reason\":\"room_name_conflict\""));
            Assert.That(calls, Is.EqualTo(1)); Assert.That(_http.Requests.Count, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator RegistrationHttpFailureIsReportedWithoutFallbackOrRetry() => Run(async () =>
        {
            Configure((request, ct) => Task.FromResult(Prepared(request.RoomName)));
            _http.Handler = (_, ct) => Task.FromResult(new PlayServGameServerHttpResponse
                { StatusCode = 409, Body = "{\"code\":\"room_owned_by_other_instance\"}" });
            await PlayServGameServer.Uplink.ConnectAsync(); Request("owned"); await Until(() => _outcomes.Count == 1);
            Assert.That(_outcomes.Single().Error.SourceCode, Is.EqualTo("room_owned_by_other_instance"));
            Assert.That(_http.Requests.Count, Is.EqualTo(1));
            Assert.That(_socket.Sent.Count(s => s.Contains("room_create_result")), Is.EqualTo(1));
        });
    }
}
