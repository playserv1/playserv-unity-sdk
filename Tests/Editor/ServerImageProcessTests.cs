using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
namespace Playserv.Editor.Tests
{
    public class ServerImageProcessTests
    {
        private static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
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
