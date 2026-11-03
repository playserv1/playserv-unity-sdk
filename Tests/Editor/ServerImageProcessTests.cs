using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
namespace Playserv.Editor.Tests
{
    public class ServerImageProcessTests
    {
        private static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
        [Test, Explicit("Requires Docker Desktop and access to a public base image.")]
        public void MinimalUnityPathCanResolveBaseImageCredentials() => Run(async () =>
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.OSXEditor) Assert.Ignore("Docker Desktop macOS fixture.");
            var previous = Environment.GetEnvironmentVariable("PATH");
            var config = Path.Combine(Path.GetTempPath(), "psv-docker-helper-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "config.json"), "{\"credsStore\":\"desktop\",\"cliPluginsExtraDirs\":[\"/Applications/Docker.app/Contents/Resources/cli-plugins\"]}");
            try
            {
                Environment.SetEnvironmentVariable("PATH", "/usr/bin:/bin:/usr/sbin:/sbin");
                var runner = new ServerImageProcess(s => s);
                var result = await runner.RunAsync(new[] { "build", "--call=outline", "--progress=plain", "-" }, null,
                    new Dictionary<string, string> { ["DOCKER_CONFIG"] = config }, "FROM alpine:3.20\n", TimeSpan.FromMinutes(2), default);
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(Environment.GetEnvironmentVariable("PATH"), Is.EqualTo("/usr/bin:/bin:/usr/sbin:/sbin"));
            }
            finally { Environment.SetEnvironmentVariable("PATH", previous); Directory.Delete(config, true); }
        });
        [Test] public void DockerResolutionPrefersPathAndFallsBackToDesktopWithoutChangingEnvironment()
        {
            Assert.That(DockerExecutable.Resolve("/custom/bin", true, "/Users/fixture", false,
                p => p == "/custom/bin/docker" || p == "/Applications/Docker.app/Contents/Resources/bin/docker", _ => true),
                Is.EqualTo("/custom/bin/docker"));
            Assert.That(DockerExecutable.Resolve("/usr/bin:/bin", true, "/Users/fixture", false,
                p => p == "/Users/fixture/.docker/bin/docker", _ => true), Is.EqualTo("/Users/fixture/.docker/bin/docker"));
        }
        [Test] public void DockerResolutionDistinguishesMissingAndNonExecutableFiles()
        {
            Assert.Throws<FileNotFoundException>(() => DockerExecutable.Resolve("/bin", false, "", false, _ => false, _ => true));
            Assert.Throws<UnauthorizedAccessException>(() => DockerExecutable.Resolve("/bin", false, "", false, _ => true, _ => false));
        }
        [Test] public void DockerDesktopIsFoundWithTheUnityHubPathOnMacOS() => Run(async () =>
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.OSXEditor ||
                !File.Exists("/Applications/Docker.app/Contents/Resources/bin/docker"))
                Assert.Ignore("Requires macOS with Docker Desktop installed.");
            var previous = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", "/usr/bin:/bin:/usr/sbin:/sbin");
                var runner = new ServerImageProcess(s => s);
                var result = await runner.RunAsync(new[] { "--version" }, null, null, null, TimeSpan.FromSeconds(10), default);
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(result.Output, Does.Contain("Docker version"));
                Assert.That(Environment.GetEnvironmentVariable("PATH"), Is.EqualTo("/usr/bin:/bin:/usr/sbin:/sbin"));
            }
            finally { Environment.SetEnvironmentVariable("PATH", previous); }
        });
        [Test] public void ActualProcessPreservesSpacesQuotesBackslashesAndShellCharacters() => Run(async () =>
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process fixture; Windows quoting is exercised by the Windows Docker smoke.");
            var value = "space value \"quoted\" back\\slash trailing\\ $(touch should-not-exist) `echo no` ; &";
            var runner = new ServerImageProcess(s => s, executable: "/usr/bin/printf");
            var result = await runner.RunAsync(new[] { "%s", value }, null, null, null, TimeSpan.FromSeconds(5), default);
            Assert.That(result.ExitCode, Is.Zero); Assert.That(result.Output, Is.EqualTo(value));
        });
        [Test] public void ActualProcessReceivesSecretOnlyOnStdinAndRedactsBothLogsAndResult() => Run(async () =>
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX stdin fixture.");
            var logs = new List<string>(); var runner = new ServerImageProcess(s => s.Replace("top-secret", "[redacted]"), s => logs.Add(s), "/bin/cat");
            var result = await runner.RunAsync(Array.Empty<string>(), null, null, "top-secret\n", TimeSpan.FromSeconds(5), default);
            Assert.That(result.Output, Does.Not.Contain("top-secret")); Assert.That(string.Join("", logs), Does.Contain("[redacted]"));
        });
        [Test] public void ActualChildDoesNotInheritPlayServCredentials() => Run(async () =>
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX environment fixture.");
            const string key = "PLAYSERV_IMAGE_TEST_SECRET"; Environment.SetEnvironmentVariable(key, "test-value");
            try { var r = new ServerImageProcess(s => s, executable: "/usr/bin/env"); var result = await r.RunAsync(Array.Empty<string>(), null, null, null, TimeSpan.FromSeconds(5), default); Assert.That(result.Output, Does.Not.Contain(key)); }
            finally { Environment.SetEnvironmentVariable(key, null); }
        });
        [Test] public void CancellationTerminatesARealProcess() => Run(async () =>
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process fixture.");
            using (var cancel = new CancellationTokenSource(100))
            {
                var r = new ServerImageProcess(s => s, executable: "/bin/sleep");
                Assert.That(() => Run(() => r.RunAsync(new[] { "20" }, null, null, null, TimeSpan.FromSeconds(30), cancel.Token)), Throws.InstanceOf<OperationCanceledException>());
            }
            await Task.CompletedTask;
        });
        [Test] public void CancellationAlsoTerminatesDescendantsHoldingOutputPipes() => Run(async () =>
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            using (var cancel = new CancellationTokenSource(150))
            {
                var r = new ServerImageProcess(s => s, executable: "/bin/sh");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Assert.That(() => Run(() => r.RunAsync(new[] { "-c", "sleep 4 & wait" }, null, null, null, TimeSpan.FromSeconds(10), cancel.Token)), Throws.InstanceOf<OperationCanceledException>());
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "A descendant retained the output pipes after cancellation.");
            }
            await Task.CompletedTask;
        });
        [Test] public void DeadlineTerminatesARealProcess() => Run(async () =>
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process fixture.");
            var r = new ServerImageProcess(s => s, executable: "/bin/sleep");
            Assert.Throws<TimeoutException>(() => Run(() => r.RunAsync(new[] { "20" }, null, null, null, TimeSpan.FromMilliseconds(100), default)));
            await Task.CompletedTask;
        });
        [Test] public void CancellationStopsReadyDescendantsWhenWorkerThreadsAreBusy()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            WithTwoAvailableWorkers(() =>
            {
                using (var fixture = new ProcessTreeFixture())
                using (var cancel = new CancellationTokenSource())
                {
                    var task = fixture.Start(cancel.Token, TimeSpan.FromSeconds(15));
                    fixture.WaitUntilReady();
                    var watch = Stopwatch.StartNew();
                    cancel.Cancel();
                    Assert.That(() => task.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
                    Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "Cancellation waited for an owned child to exit naturally.");
                    fixture.AssertStopped();
                }
            });
        }

        [Test] public void DeadlineStopsReadyDescendantsAndReleasesTheirOutputPipes()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            WithTwoAvailableWorkers(() =>
            {
                using (var fixture = new ProcessTreeFixture())
                {
                    var watch = Stopwatch.StartNew();
                    var task = fixture.Start(CancellationToken.None, TimeSpan.FromMilliseconds(750));
                    fixture.WaitUntilReady();
                    Assert.That(() => task.GetAwaiter().GetResult(), Throws.TypeOf<TimeoutException>());
                    Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "Deadline waited for a descendant's natural exit.");
                    fixture.AssertStopped();
                }
            });
        }

        [Test] public void CancellationStopsTheTreeWhileStandardInputIsBlocked()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            using (var fixture = new ProcessTreeFixture())
            using (var cancel = new CancellationTokenSource())
            {
                var task = fixture.Start(cancel.Token, TimeSpan.FromSeconds(15), new string('x', 4 * 1024 * 1024));
                fixture.WaitUntilReady();
                var watch = Stopwatch.StartNew();
                cancel.Cancel();
                Assert.That(() => task.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
                fixture.AssertStopped();
            }
        }

        [Test] public void CancellationLeavesAnotherCommandAlive()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            using (var target = new ProcessTreeFixture())
            using (var other = new ProcessTreeFixture())
            using (var cancelTarget = new CancellationTokenSource())
            using (var cancelOther = new CancellationTokenSource())
            {
                var targetTask = target.Start(cancelTarget.Token, TimeSpan.FromSeconds(15));
                var otherTask = other.Start(cancelOther.Token, TimeSpan.FromSeconds(15));
                try
                {
                    target.WaitUntilReady();
                    other.WaitUntilReady();
                    cancelTarget.Cancel();
                    Assert.That(() => targetTask.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
                    target.AssertStopped();
                    other.AssertRunning();
                    Assert.That(otherTask.IsCompleted, Is.False);
                }
                finally
                {
                    cancelTarget.Cancel();
                    cancelOther.Cancel();
                    try { targetTask.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                    try { otherTask.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                }
                other.AssertStopped();
            }
        }

        [Test] public void CancellationBeforeStartDoesNotLaunchTheCommand()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process fixture.");
            var marker = Path.Combine(Path.GetTempPath(), "psv-never-start-" + Guid.NewGuid().ToString("N"));
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                var runner = new ServerImageProcess(s => s, executable: "/bin/sh");
                var task = runner.RunAsync(new[] { "-c", "touch \"$1\"", "fixture", marker }, null, null, null, TimeSpan.FromSeconds(5), cancel.Token);
                Assert.That(() => task.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
                Assert.That(File.Exists(marker), Is.False);
            }
        }

        [Test] public void NormalExitCollectsBothOutputStreamsAndPreservesTheExitCode() => Run(async () =>
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process fixture.");
            var runner = new ServerImageProcess(s => s, executable: "/bin/sh");
            var result = await runner.RunAsync(new[] { "-c", "printf 'output'; printf 'error' >&2; exit 7" }, null, null, null, TimeSpan.FromSeconds(5), default);
            Assert.That(result.Output, Is.EqualTo("outputerror"));
            Assert.That(result.ExitCode, Is.EqualTo(7));
        });

        [Test] public void StartupFailurePreservesTheOriginalError()
        {
            var runner = new ServerImageProcess(s => s, executable: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing"));
            var task = runner.RunAsync(Array.Empty<string>(), null, null, null, TimeSpan.FromSeconds(5), default);
            Assert.That(() => task.GetAwaiter().GetResult(), Throws.TypeOf<System.ComponentModel.Win32Exception>());
        }

        [Test] public void OutputCallbackFailureStopsTheOwnedProcessAndPreservesTheError()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process fixture.");
            var expected = new IOException("output callback failed");
            using (var fixture = new ProcessTreeFixture())
            using (var release = new ManualResetEventSlim())
            {
                var task = fixture.Start(CancellationToken.None, TimeSpan.FromSeconds(15),
                    script: "sleep 8 & " + ProcessTreeFixture.Ready + "printf 'output\\n'; wait \"$child\"",
                    redact: text => { release.Wait(); throw expected; });
                try { fixture.WaitUntilReady(); }
                finally { release.Set(); }
                var watch = Stopwatch.StartNew();
                Assert.That(Assert.Throws<IOException>(() => task.GetAwaiter().GetResult()), Is.SameAs(expected));
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
                fixture.AssertStopped();
            }
        }

        [Test] public void CancellationDoesNotLetTheParentSpawnAReplacementChild()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            using (var fixture = new ProcessTreeFixture())
            using (var cancel = new CancellationTokenSource())
            {
                var task = fixture.Start(cancel.Token, TimeSpan.FromSeconds(15), script:
                    "sleep 8 & " + ProcessTreeFixture.Ready + "wait \"$child\"; sleep 8 & printf '%s' \"$!\" > \"$1/replacement\"; wait");
                fixture.WaitUntilReady();
                cancel.Cancel();
                Assert.That(() => task.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
                fixture.AssertStopped();
                Assert.That(fixture.ReplacementIsRunning, Is.False, "Cleanup let the parent launch another live child.");
            }
        }

        [Test] public void CancellationKeepsTheCallerTokenWhenOutputFails()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            using (var fixture = new ProcessTreeFixture())
            using (var cancel = new CancellationTokenSource())
            using (var release = new ManualResetEventSlim())
            {
                var task = fixture.Start(cancel.Token, TimeSpan.FromSeconds(15),
                    script: "sleep 8 & " + ProcessTreeFixture.Ready + "printf 'output\\n'; wait \"$child\"",
                    redact: text => { release.Wait(); cancel.Cancel(); throw new IOException("output failed during cancellation"); });
                try { fixture.WaitUntilReady(); }
                finally { release.Set(); }
                var watch = Stopwatch.StartNew();
                OperationCanceledException failure = null;
                try { task.GetAwaiter().GetResult(); }
                catch (OperationCanceledException error) { failure = error; }
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure.CancellationToken, Is.EqualTo(cancel.Token));
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
                fixture.AssertStopped();
            }
        }

        [Test] public void CancellationReturnsAfterOwnedProcessCleanup()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            using (var fixture = new ProcessTreeFixture())
            using (var cancel = new CancellationTokenSource())
            {
                var task = fixture.Start(cancel.Token, TimeSpan.FromSeconds(15));
                fixture.WaitUntilReady();
                try
                {
                    cancel.Cancel();
                    fixture.AssertStoppedImmediately();
                }
                finally { try { task.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
            }
        }

        [Test] public void CancellationWaitsForExitWhenOutputPipesAreAlreadyClosed()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            for (var attempt = 0; attempt < 20; attempt++)
            {
                using (var fixture = new ProcessTreeFixture())
                using (var cancel = new CancellationTokenSource())
                {
                    var task = fixture.Start(cancel.Token, TimeSpan.FromSeconds(15), script:
                        "exec 1>&- 2>&-; sleep 8 & " + ProcessTreeFixture.Ready + "wait \"$child\"");
                    fixture.WaitUntilReady();
                    try
                    {
                        cancel.Cancel();
                        fixture.AssertStoppedImmediately();
                    }
                    finally { try { task.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
                }
            }
        }

        [Test] public void CancelledTaskDoesNotLogOutputFromAChildAfterItsParentExited()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("POSIX process-tree fixture.");
            using (var fixture = new ProcessTreeFixture())
            using (var cancel = new CancellationTokenSource())
            {
                var logs = 0;
                var task = fixture.Start(cancel.Token, TimeSpan.FromSeconds(15), script:
                    "(sleep 2; printf 'late output\\n'; printf 'sent' > \"$1/emitted\") & " + ProcessTreeFixture.Ready + "exit 0",
                    log: text => Interlocked.Increment(ref logs));
                fixture.WaitUntilReady(false);
                fixture.WaitForParentExit();
                var watch = Stopwatch.StartNew();
                cancel.Cancel();
                Assert.That(() => task.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
                fixture.WaitForEmission();
                Thread.Sleep(100);
                Assert.That(Volatile.Read(ref logs), Is.Zero, "An output callback ran after the cancellation task completed.");
            }
        }

        private static void WithTwoAvailableWorkers(Action action)
        {
            ThreadPool.GetMinThreads(out var minimum, out var minimumIo);
            ThreadPool.GetMaxThreads(out var maximum, out var maximumIo);
            var workerCount = Math.Max(2, Environment.ProcessorCount);
            using (var blocked = new CountdownEvent(workerCount - 2))
            using (var release = new ManualResetEventSlim())
            {
                var workers = new Task[workerCount - 2];
                try
                {
                    // Unity runs these synchronous EditMode tests sequentially. Respect Mono's CPU floor.
                    Assert.That(ThreadPool.SetMinThreads(workerCount, minimumIo), Is.True);
                    Assert.That(ThreadPool.SetMaxThreads(workerCount, maximumIo), Is.True);
                    for (var i = 0; i < workers.Length; i++)
                        workers[i] = Task.Run(() => { blocked.Signal(); release.Wait(); });
                    Assert.That(blocked.Wait(TimeSpan.FromSeconds(5)), Is.True, "The worker-pool contention fixture did not start.");
                    action();
                }
                finally
                {
                    release.Set();
                    ThreadPool.SetMaxThreads(maximum, maximumIo);
                    ThreadPool.SetMinThreads(minimum, minimumIo);
                    foreach (var worker in workers) worker?.GetAwaiter().GetResult();
                }
            }
        }

        private sealed class ProcessTreeFixture : IDisposable
        {
            private readonly string _directory = Path.Combine(Path.GetTempPath(), "psv-process-tree-" + Guid.NewGuid().ToString("N"));
            internal const string Ready = "child=$!; printf '%s\\n%s\\n' \"$$\" \"$child\" > \"$1/ready\"; ";
            private int _parent;
            private int _child;

            internal ProcessTreeFixture() { Directory.CreateDirectory(_directory); }

            internal Task<ServerImageProcessResult> Start(CancellationToken cancellation, TimeSpan timeout, string input = null,
                string script = null, Func<string, string> redact = null, Action<string> log = null)
            {
                var runner = new ServerImageProcess(redact ?? (s => s), log, "/bin/sh");
                return runner.RunAsync(new[] { "-c", script ?? "sleep 8 & " + Ready + "wait \"$child\"; exit 0", "fixture", _directory }, null, null, input, timeout, cancellation);
            }

            internal void WaitUntilReady(bool parentMustBeRunning = true)
            {
                var watch = Stopwatch.StartNew();
                var ready = Path.Combine(_directory, "ready");
                while (watch.Elapsed < TimeSpan.FromSeconds(5))
                {
                    if (File.Exists(ready))
                    {
                        var ids = File.ReadAllLines(ready);
                        if (ids.Length == 2 && int.TryParse(ids[0], out var parent) && int.TryParse(ids[1], out var child))
                        {
                            _parent = parent;
                            _child = child;
                            if (parentMustBeRunning) Assert.That(IsRunning(_parent), Is.True);
                            Assert.That(IsRunning(_child), Is.True);
                            return;
                        }
                    }
                    Thread.Sleep(10);
                }
                Assert.Fail("The real parent and child did not report readiness.");
            }

            internal void AssertStopped()
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromMilliseconds(250) && (IsRunning(_parent) || IsRunning(_child))) Thread.Sleep(10);
                AssertStoppedImmediately();
            }

            internal void AssertStoppedImmediately()
            {
                Assert.That(IsRunning(_parent), Is.False, "Owned parent is still running.");
                Assert.That(IsRunning(_child), Is.False, "Owned child is still running.");
            }

            internal bool ReplacementIsRunning => IsRunning(ReplacementPid);
            private int ReplacementPid => File.Exists(Path.Combine(_directory, "replacement")) &&
                int.TryParse(File.ReadAllText(Path.Combine(_directory, "replacement")), out var id) ? id : 0;

            internal void WaitForParentExit()
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(2) && IsRunning(_parent)) Thread.Sleep(10);
                Assert.That(IsRunning(_parent), Is.False);
            }

            internal void WaitForEmission()
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(3) && !File.Exists(Path.Combine(_directory, "emitted"))) Thread.Sleep(10);
                Assert.That(File.Exists(Path.Combine(_directory, "emitted")), Is.True);
            }
            internal void AssertRunning()
            {
                Assert.That(IsRunning(_parent), Is.True, "Unrelated parent was terminated.");
                Assert.That(IsRunning(_child), Is.True, "Unrelated child was terminated.");
            }

            private static bool IsRunning(int id)
            {
                if (id == 0 || (kill(id, 0) != 0 && Marshal.GetLastWin32Error() == 3)) return false;
                if (Directory.Exists("/proc"))
                {
                    try
                    {
                        var stat = File.ReadAllText("/proc/" + id + "/stat");
                        return stat[stat.LastIndexOf(')') + 2] != 'Z';
                    }
                    catch (IOException) { return false; }
                }
                using (var ps = Process.Start(new ProcessStartInfo("/bin/ps", "-o stat= -p " + id) { UseShellExecute = false, RedirectStandardOutput = true }))
                {
                    var state = ps.StandardOutput.ReadToEnd().Trim();
                    ps.WaitForExit(1000);
                    return state.Length > 0 && !state.StartsWith("Z", StringComparison.Ordinal);
                }
            }
            [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);

            public void Dispose()
            {
                foreach (var id in new[] { ReplacementPid, _child, _parent })
                {
                    if (IsRunning(id)) kill(id, 9);
                }
                Directory.Delete(_directory, true);
            }
        }
        [Test] public void DoubleClickAndCancelledLateCompletionCannotApplyResults() => Run(async () =>
        {
            using (var op = new ServerImageOperation())
            {
                var ready = new TaskCompletionSource<Action>(); var applied = 0;
                var first = op.TryRunAsync(_ => ready.Task, _ => applied += 100);
                Assert.That(op.Running, Is.True);
                Assert.That(await op.TryRunAsync(_ => Task.FromResult<Action>(() => applied++), _ => applied += 100), Is.False);
                op.Cancel(); ready.SetResult(() => applied++); await first;
                Assert.That(applied, Is.Zero); Assert.That(op.Running, Is.False);
                Assert.That(await op.TryRunAsync(_ => Task.FromResult<Action>(() => applied++), _ => applied += 100), Is.True);
                Assert.That(applied, Is.EqualTo(1));
            }
        });
        [Test] public void DisposalDropsLateFailuresAndPreventsNewOperations() => Run(async () =>
        {
            var op = new ServerImageOperation(); var source = new TaskCompletionSource<Action>(); var errors = 0;
            var task = op.TryRunAsync(_ => source.Task, _ => errors++); op.Dispose(); source.SetException(new IOException("late")); await task;
            Assert.That(errors, Is.Zero); Assert.That(await op.TryRunAsync(_ => Task.FromResult<Action>(() => { }), _ => errors++), Is.False);
        });
    }
}
