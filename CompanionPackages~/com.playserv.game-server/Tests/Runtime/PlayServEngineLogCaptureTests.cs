using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServEngineLogCaptureTests
    {
        private H.Socket _socket;
        private Socket _wire;
        private double _now;
        [SetUp] public void Setup() => PlayServGameServer.CancelForModuleShutdown();
        [TearDown] public void Cleanup()
        {
            _wire?.Release.TrySetResult(true);
            PlayServGameServer.CancelForModuleShutdown();
            PlayServGameServer.SetSupportedBuildForTesting(null);
        }

        private PlayServEngineLogOptions Configure(bool capture, string setting = null, object value = null, int count = 256, int bytes = 1024 * 1024, int timeout = 1000)
        {
            var options = new PlayServGameServerOptions
            {
                BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(),
                LogQueueCapacity = count, LogQueueMaxBytes = bytes, HttpTimeout = TimeSpan.FromMilliseconds(timeout)
            };
            var settings = options.EngineLogs;
            settings.Enabled = capture; settings.FlushInterval = TimeSpan.FromMilliseconds(25);
            if (setting != null) typeof(PlayServEngineLogOptions).GetProperty(setting).SetValue(settings, value);
            PlayServGameServer.ConfigureForTesting(options, new H.Http(), () => DateTimeOffset.UtcNow, (d, ct) => Task.Delay(d, ct));
            PlayServGameServer.GetContextForServices().MonotonicSeconds = () => _now;
            PlayServGameServer.Uplink.SocketFactory = () => _wire = new Socket(_socket = new H.Socket(H.Ack()));
            return settings;
        }

        private static long Counter(string name)
        {
            var snapshot = PlayServGameServer.Logs.EngineCapture;
            return Convert.ToInt64(snapshot.GetType().GetProperty(name).GetValue(snapshot));
        }
        private string[] Sent => _socket.Sent.Where(s => s.Contains("\"type\":\"log\"")).ToArray();
        private static string[] Queued()
        {
            var logs = PlayServGameServer.Logs;
            var gate = typeof(PlayServGameServerLogs).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(logs);
            lock (gate)
            {
                var state = typeof(PlayServGameServerLogs).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(logs);
                var queue = (IEnumerable)state.GetType().GetField("Queue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
                return queue.Cast<byte[]>().Select(Encoding.UTF8.GetString).ToArray();
            }
        }

        [UnityTest] public IEnumerator EnabledEngineLogsArriveWithoutManualWriteOrFlush() => H.Run(async () =>
        {
            Configure(true); await PlayServGameServer.Uplink.ConnectAsync();
            Debug.Log("engine-log-psv3727");
            await Task.Delay(600);
            Assert.That(_socket.Sent.Any(s => s.Contains("\"type\":\"log\"") && s.Contains("engine-log-psv3727")), Is.True);
        });

        [UnityTest] public IEnumerator WarningUsesTheUplinkContractLevel() => H.Run(async () =>
        {
            Configure(false); await PlayServGameServer.Uplink.ConnectAsync();
            PlayServGameServer.Logs.TryWrite("warning-contract", PlayServServerLogLevel.Warning);
            await PlayServGameServer.Logs.FlushAsync();
            Assert.That(_socket.Sent.Last(), Does.Contain("\"level\":\"warn\""));
        });

        [Test] public void ReservationQueryAndRefreshTokenAreRedactedBeforeRetention()
        {
            var text = PlayServGameServerLogs.Text("Join?rsv=opaque-ticket&mode=duel rt_refreshsecret");
            Assert.That(text, Does.Not.Contain("opaque-ticket").And.Not.Contain("rt_refreshsecret"));
            Assert.That(text, Does.Contain("mode=duel"));
        }

        [UnityTest] public IEnumerator DisabledCaptureLeavesManualLoggingExplicit() => H.Run(async () =>
        {
            Configure(false); await PlayServGameServer.Uplink.ConnectAsync();
            Debug.Log("disabled-engine"); PlayServGameServer.Logs.TryWrite("manual-entry");
            await Task.Delay(100);
            Assert.That(Sent, Is.Empty); Assert.That(PlayServGameServer.Logs.PendingCount, Is.EqualTo(1));
            await PlayServGameServer.Logs.FlushAsync(); Assert.That(Sent.Single(), Does.Contain("manual-entry"));
        });

        [UnityTest] public IEnumerator PlayerBuildNeverSubscribesEvenWhenEnabled() => H.Run(async () =>
        {
            PlayServGameServer.SetSupportedBuildForTesting(false); Configure(true);
            Debug.Log("player-client-log"); await Task.Delay(100);
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero);
            Assert.That(Counter("IsCapturing"), Is.Zero);
        });

        [UnityTest] public IEnumerator AllUnitySeveritiesAndExceptionStackUseStructuredFrames() => H.Run(async () =>
        {
            Configure(true); await PlayServGameServer.Uplink.ConnectAsync();
            Debug.Log("mapped-info");
            LogAssert.Expect(LogType.Warning, "mapped-warning"); Debug.LogWarning("mapped-warning");
            LogAssert.Expect(LogType.Error, "mapped-error"); Debug.LogError("mapped-error");
            LogAssert.Expect(LogType.Assert, "mapped-assert"); Debug.LogAssertion("mapped-assert");
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: mapped-exception"));
            Debug.LogException(new InvalidOperationException("mapped-exception"));
            await H.Until(() => Sent.Count(s => s.Contains("mapped-")) == 5);
            Assert.That(Sent.Single(s => s.Contains("mapped-info")), Does.Contain("\"level\":\"info\""));
            Assert.That(Sent.Single(s => s.Contains("mapped-warning")), Does.Contain("\"level\":\"warn\""));
            foreach (var label in new[] { "mapped-error", "mapped-assert", "mapped-exception" })
                Assert.That(Sent.Single(s => s.Contains(label)), Does.Contain("\"level\":\"error\""));
            Assert.That(Sent.Single(s => s.Contains("mapped-exception")), Does.Contain("stack_trace"));
        });

        [Test] public void MinimumLevelFiltersBeforeQueueing()
        {
            Configure(true, "MinimumLevel", PlayServServerLogLevel.Error);
            Debug.Log("filtered-info"); LogAssert.Expect(LogType.Warning, "filtered-warning"); Debug.LogWarning("filtered-warning");
            LogAssert.Expect(LogType.Error, "kept-error"); Debug.LogError("kept-error");
            Assert.That(Queued(), Has.Length.EqualTo(1)); Assert.That(Queued()[0], Does.Contain("kept-error"));
            Assert.That(Counter("FilteredCount"), Is.EqualTo(2));
        }

        [Test] public void EngineMessageIsRedactedBeforeQueueingAndTruncationKeepsUnicodeValid()
        {
            Configure(true, "MaxTextBytes", 80);
            Debug.Log("engine rsv=opaque-secret&x=1 rt_refresh sk_private pk_private Bearer opaque-bearer " + new string('ї', 100));
            var frame = Queued().Single();
            foreach (var secret in new[] { "opaque-secret", "rt_refresh", "sk_private", "pk_private", "opaque-bearer" })
                Assert.That(frame, Does.Not.Contain(secret));
            var json = GameServerServiceValues.Object(frame);
            Assert.That(Encoding.UTF8.GetByteCount((string)json["message"]), Is.LessThanOrEqualTo(80));
            Assert.That(frame.IndexOf('\uFFFD'), Is.EqualTo(-1));
        }

        [Test] public void RateLimitUsesMonotonicWindowsAndReportsDrops()
        {
            Configure(true, "MaxEntriesPerWindow", 2);
            Debug.Log("rate-one"); Debug.Log("rate-two"); Debug.Log("rate-three");
            Assert.That(Counter("RateLimitedCount"), Is.EqualTo(1));
            Assert.That(Queued(), Has.Length.EqualTo(2));
            _now += 10; Debug.Log("rate-next-window");
            Assert.That(Queued(), Has.Length.EqualTo(3)); Assert.That(Counter("AcceptedCount"), Is.EqualTo(3));
        }

        [Test] public void CaptureHonorsBothQueueBoundsAndReportsRejectedEntries()
        {
            Configure(true, count: 1); Debug.Log("queue-one"); Debug.Log("queue-two");
            Assert.That(Queued(), Has.Length.EqualTo(1)); Assert.That(Counter("QueueRejectedCount"), Is.EqualTo(1));
            Assert.That(PlayServGameServer.Logs.DroppedCount, Is.EqualTo(1));
            Configure(true, bytes: 80); Debug.Log(new string('ї', 100));
            Assert.That(Queued(), Is.Empty); Assert.That(Counter("QueueRejectedCount"), Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator WorkerThreadsCanCaptureWithoutMainThreadQueue() => H.Run(async () =>
        {
            Configure(true);
            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => Debug.Log("worker-entry-" + i))));
            Assert.That(Queued().Count(s => s.Contains("worker-entry-")), Is.EqualTo(20));
        });

        [UnityTest] public IEnumerator OfflineLogsAreDeliveredOnReconnectOnlyOnce() => H.Run(async () =>
        {
            Configure(true); await PlayServGameServer.Uplink.ConnectAsync();
            await PlayServGameServer.Uplink.DisconnectAsync();
            Debug.Log("offline-entry"); await Task.Delay(100);
            Assert.That(Queued().Count(s => s.Contains("offline-entry")), Is.EqualTo(1));
            await PlayServGameServer.Uplink.ConnectAsync();
            await H.Until(() => Sent.Any(s => s.Contains("offline-entry")));
            await Task.Delay(100); Assert.That(Sent.Count(s => s.Contains("offline-entry")), Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator AsyncSendingDiagnosticsCannotRecursivelyForwardThemselves() => H.Run(async () =>
        {
            Configure(true); await PlayServGameServer.Uplink.ConnectAsync();
            _wire.OnLog = async () => { await Task.Yield(); Debug.Log("sending-diagnostic"); };
            Debug.Log("original-entry"); await H.Until(() => Sent.Any(s => s.Contains("original-entry")));
            await Task.Delay(100); Assert.That(Sent.Any(s => s.Contains("sending-diagnostic")), Is.False);
            Assert.That(_wire.Attempts, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator AmbiguousAutomaticSendIsDroppedWithoutRetrying() => H.Run(async () =>
        {
            Configure(true); await PlayServGameServer.Uplink.ConnectAsync(); _wire.FailNext = true; var first = _wire;
            Debug.Log("ambiguous-entry"); await H.Until(() => PlayServGameServer.Logs.DroppedCount == 1);
            Debug.Log("next-entry"); await H.Until(() => Sent.Any(s => s.Contains("next-entry")));
            Assert.That(Sent.Any(s => s.Contains("ambiguous-entry")), Is.False);
            Assert.That(first.Attempts, Is.EqualTo(1)); Assert.That(_wire.Attempts, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator ShutdownDrainsEntriesOutsideTheCurrentFlushSnapshot() => H.Run(async () =>
        {
            Configure(true); await PlayServGameServer.Uplink.ConnectAsync(); _wire.Block = true;
            Debug.Log("closing-one"); await H.Until(() => _wire.Attempts == 1);
            Debug.Log("closing-two"); var closing = PlayServGameServer.ShutdownAsync();
            _wire.Release.TrySetResult(true); var result = await closing;
            Assert.That(result.LogsError.IsError, Is.False);
            Assert.That(Sent.Count(s => s.Contains("closing-")), Is.EqualTo(2));
            Debug.Log("after-shutdown"); Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero);
        });

        [UnityTest] public IEnumerator StalledShutdownHasABoundedFlushAndSafeError() => H.Run(async () =>
        {
            Configure(true, timeout: 80); await PlayServGameServer.Uplink.ConnectAsync(); _wire.Block = true;
            Debug.Log("stalled-close"); await H.Until(() => _wire.Attempts == 1);
            var closing = PlayServGameServer.ShutdownAsync();
            Assert.That(await Task.WhenAny(closing, Task.Delay(1000)), Is.SameAs(closing));
            Assert.That((await closing).LogsError.IsError, Is.True);
            Assert.That(Counter("IsCapturing"), Is.Zero);
        });

        [Test] public void ReconfigurationDisablesAndResetsCaptureWithoutDuplicateSubscriptions()
        {
            Configure(true); Configure(true); Debug.Log("single-subscription");
            Assert.That(Queued(), Has.Length.EqualTo(1));
            Configure(false); Debug.Log("disabled-after-configure");
            Assert.That(Queued(), Is.Empty); Assert.That(Counter("IsCapturing"), Is.Zero);
        }

        [Test] public void ModuleShutdownDetachesCaptureImmediately()
        {
            Configure(true); Debug.Log("before-module-stop");
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.EqualTo(1));
            PlayServGameServer.CancelForModuleShutdown(); Debug.Log("after-module-stop");
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero); Assert.That(Counter("IsCapturing"), Is.Zero);
        }

        [Test] public void ConfigurationIsSnapshottedAndInvalidReplacementKeepsCurrentCapture()
        {
            var options = Configure(true);
            options.Enabled = false; options.MinimumLevel = PlayServServerLogLevel.Error;
            Debug.Log("snapshot-entry"); Assert.That(Queued(), Has.Length.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Configure(true, "MaxEntriesPerWindow", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Configure(true, "MaxTextBytes", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Configure(true, "FlushInterval", TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => Configure(true, "RateWindow", TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => Configure(true, "MinimumLevel", (PlayServServerLogLevel)99));
            Debug.Log("still-current-entry"); Assert.That(Queued(), Has.Length.EqualTo(2));
        }

        [Test] public void ErrorStackCanBeDisabled()
        {
            Configure(true, "IncludeStackTrace", false);
            LogAssert.Expect(LogType.Error, "no-stack-entry"); Debug.LogError("no-stack-entry");
            Assert.That(Queued().Single(), Does.Not.Contain("stack_trace"));
        }

        [Test] public void CapturedStackAndLaunchCredentialsAreRedactedBeforeRetention()
        {
            Configure(true);
            var capture = typeof(PlayServGameServerLogs).GetMethod("Capture", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(PlayServGameServer.Logs, null);
            var callback = typeof(PlayServEngineLogCapture).GetMethod("OnLog", BindingFlags.Instance | BindingFlags.NonPublic);
            callback.Invoke(capture, new object[] { "server_key=opaque-server deployment_token=opaque-deploy", "at https://game?rsv=opaque-ticket&x=1 rt_opaque", LogType.Exception });
            var frame = Queued().Single();
            foreach (var secret in new[] { "opaque-server", "opaque-deploy", "opaque-ticket", "rt_opaque" }) Assert.That(frame, Does.Not.Contain(secret));
            Assert.That(frame, Does.Contain("stack_trace"));
            Configure(true);
            callback.Invoke(capture, new object[] { "stale-callback", "old-stack", LogType.Error });
            Assert.That(Queued(), Is.Empty);
        }

        [UnityTest] public IEnumerator OldAutomaticFlushCannotDrainANewDisabledConfiguration() => H.Run(async () =>
        {
            Configure(true); await PlayServGameServer.Uplink.ConnectAsync(); _wire.Block = true;
            Debug.Log("old-generation"); await H.Until(() => _wire.Attempts == 1); var old = _wire;
            PlayServGameServer.CancelForModuleShutdown(); Configure(false);
            await PlayServGameServer.Uplink.ConnectAsync(); PlayServGameServer.Logs.TryWrite("new-manual-entry");
            old.Release.TrySetResult(true); await Task.Delay(100);
            Assert.That(PlayServGameServer.Logs.PendingCount, Is.EqualTo(1)); Assert.That(Sent, Is.Empty);
        });

        [UnityTest] public IEnumerator WorkerConfigurationCannotReplaceCaptureOrTouchUnitySubscriptions() => H.Run(async () =>
        {
            Configure(true); var context = PlayServGameServer.GetContextForServices();
            Assert.That(Captures(), Has.Length.EqualTo(1), "Inspect the real Unity event, not a substituted event source.");
            try
            {
                var error = await Task.Run(() =>
                { try { Configure(true); return null; } catch (Exception ex) { return ex; } });
                Assert.That(error, Is.TypeOf<InvalidOperationException>());
                Assert.That(PlayServGameServer.GetContextForServices(), Is.SameAs(context));
                Assert.That(Captures(), Has.Length.EqualTo(1));
            }
            finally { foreach (var capture in Captures()) capture.Dispose(); }
        });

        [UnityTest] public IEnumerator WorkerShutdownDetachesOnUnityThreadAndOldContextCannotReviveCapture() => H.Run(async () =>
        {
            Configure(true); var old = PlayServGameServer.GetContextForServices();
            await Task.Run(() => PlayServGameServer.CancelForModuleShutdown());
            await H.Until(() => Captures().Length == 0);
            PlayServGameServer.Logs.ResetForConfiguration(old);
            Debug.Log("stale-config-after-stop");
            Assert.That(Captures(), Is.Empty); Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero);
        });

        [Test] public void StaleConfigurationCannotOverrideTheCurrentCapturePolicy()
        {
            Configure(true); var old = PlayServGameServer.GetContextForServices();
            Configure(false); PlayServGameServer.Logs.ResetForConfiguration(old);
            Debug.Log("stale-config-after-reconfigure");
            Assert.That(Captures(), Is.Empty); Assert.That(PlayServGameServer.Logs.PendingCount, Is.Zero);
        }

        [Test] public void StructuredEngineSecretsUseTheManualLoggersRedactionRules()
        {
            Configure(true);
            Debug.Log("{\"client_secret\":\"opaque-private-value\",\"customCredential\":\"opaque-credential\",\"nested\":[{\"customToken\":\"opaque-custom\"}]}");
            var frame = Queued().Single();
            foreach (var secret in new[] { "opaque-private-value", "opaque-credential", "opaque-custom" })
                Assert.That(frame, Does.Not.Contain(secret));
        }

        [Test] public void SubMillisecondIntervalIsRejectedWithoutCreatingABusyLoop()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayServEngineLogOptions
                { Enabled = true, FlushInterval = TimeSpan.FromTicks(1) }.Snapshot());
        }

        private static PlayServEngineLogCapture[] Captures() => typeof(Application)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType))
            .Select(f => f.GetValue(null) as Delegate).Where(d => d != null)
            .SelectMany(d => d.GetInvocationList()).Select(d => d.Target)
            .OfType<PlayServEngineLogCapture>().Distinct().ToArray();

#if UNITY_EDITOR
        [Test] public void LeavingEditorPlayModeDetachesWithoutDependingOnDomainReload()
        {
            Configure(true);
            PlayServEngineLogCapture.OnEditorStateChanged(UnityEditor.PlayModeStateChange.ExitingPlayMode);
            Debug.Log("after-editor-stop"); Assert.That(Queued(), Is.Empty);
            Assert.That(PlayServGameServer.Logs.EngineCapture.IsCapturing, Is.False);
        }
#endif

        private sealed class Socket : IPlayServUplinkSocket
        {
            private readonly H.Socket _inner;
            internal int Attempts;
            internal bool Block, FailNext;
            internal Func<Task> OnLog;
            internal readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Socket(H.Socket inner) { _inner = inner; }
            public Task ConnectAsync(Uri uri, string credential, CancellationToken ct) => _inner.ConnectAsync(uri, credential, ct);
            public async Task SendAsync(byte[] bytes, CancellationToken ct)
            {
                if (Encoding.UTF8.GetString(bytes).Contains("\"type\":\"log\""))
                {
                    Interlocked.Increment(ref Attempts);
                    if (OnLog != null) await OnLog();
                    if (Block) await Release.Task;
                    if (FailNext) { FailNext = false; throw new Exception("sk_send_secret"); }
                }
                await _inner.SendAsync(bytes, ct);
            }
            public Task<string> ReceiveAsync(CancellationToken ct) => _inner.ReceiveAsync(ct);
            public void Abort() => _inner.Abort();
            public void Dispose() => _inner.Dispose();
        }
    }
}
