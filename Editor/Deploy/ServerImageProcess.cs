using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Editor
{
    internal sealed class ServerImageProcess : IServerImageProcess
    {
        private readonly Func<string, string> _redact;
        private readonly Action<string> _log;
        private readonly string _executable;
        internal ServerImageProcess(Func<string, string> redact, Action<string> log = null, string executable = "docker")
        { _redact = redact; _log = log; _executable = executable; }

        public Task<ServerImageProcessResult> RunAsync(string[] args, string directory, IReadOnlyDictionary<string, string> environment, string input, TimeSpan timeout, CancellationToken ct)
        {
            return Task.Factory.StartNew(() =>
            {
                ct.ThrowIfCancellationRequested();
                var executable = _executable == "docker" ? DockerExecutable.Resolve() : _executable;
                var start = new ProcessStartInfo(executable, string.Join(" ", args.Select(Quote)))
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = directory ?? Path.GetTempPath()
                };
                foreach (var key in start.EnvironmentVariables.Keys.Cast<string>().ToArray())
                    if (key.StartsWith("PLAYSERV_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("TANK_REGISTRY_", StringComparison.OrdinalIgnoreCase) || key == "DOCKER_AUTH_CONFIG")
                        start.EnvironmentVariables.Remove(key);
                if (environment != null)
                    foreach (var pair in environment)
                        if (pair.Value == null) start.EnvironmentVariables.Remove(pair.Key);
                        else start.EnvironmentVariables[pair.Key] = pair.Value;
                if (_executable == "docker")
                    start.EnvironmentVariables["PATH"] = Path.GetDirectoryName(executable) + Path.PathSeparator + start.EnvironmentVariables["PATH"];
                using (var process = new Process { StartInfo = start })
                {
                    var elapsed = Stopwatch.StartNew();
                    ct.ThrowIfCancellationRequested();
                    // Mono implements pipe ReadAsync/WriteAsync with blocking ThreadPool work.
                    // Keep pipe I/O and the lifetime monitor off that pool, including the deadline.
                    var outputLifetime = new OutputLifetime();
                    var streams = Array.Empty<Task>();
                    var cleanupGate = new object();
                    var cleaned = false;
                    Action cleanup = () =>
                    {
                        outputLifetime.Close();
                        lock (cleanupGate)
                        {
                            if (cleaned) return;
                            TerminateTree(process);
                            CloseOutput(process);
                            WaitForStreams(Volatile.Read(ref streams), 500);
                            cleaned = true;
                        }
                    };
                    // Cancel must finish owned-process cleanup before an Editor domain unload.
                    using (ct.Register(cleanup))
                    {
                        var completed = false;
                        try
                        {
                            Task<string> stdout;
                            Task<string> stderr;
                            Task<bool> stdin;
                            lock (cleanupGate)
                            {
                                ct.ThrowIfCancellationRequested();
                                // Register cancellation before launching; Cancel also waits for a concurrent Start.
                                process.Start();
                                stdout = StartWorker(() => Read(process.StandardOutput, outputLifetime));
                                stderr = StartWorker(() => Read(process.StandardError, outputLifetime));
                                stdin = StartWorker(() =>
                                {
                                    if (input != null) process.StandardInput.Write(input);
                                    process.StandardInput.Close();
                                    return true;
                                });
                                Volatile.Write(ref streams, new Task[] { stdout, stderr, stdin });
                            }
                            while (true)
                            {
                                ct.ThrowIfCancellationRequested();
                                if (timeout != Timeout.InfiniteTimeSpan && elapsed.Elapsed >= timeout)
                                    throw new TimeoutException("Docker operation exceeded its time budget.");
                                foreach (var stream in streams)
                                    if (stream.IsFaulted) stream.GetAwaiter().GetResult();
                                if (process.HasExited && streams.All(stream => stream.IsCompleted))
                                {
                                    stdin.GetAwaiter().GetResult();
                                    var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                                    completed = true;
                                    return new ServerImageProcessResult { ExitCode = process.ExitCode, Output = output };
                                }
                                ct.WaitHandle.WaitOne(10);
                            }
                        }
                        catch (Exception) when (ct.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(ct);
                        }
                        finally
                        {
                            if (completed)
                            {
                                outputLifetime.Close();
                                CloseOutput(process);
                            }
                            else cleanup();
                        }
                    }
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static Task<T> StartWorker<T>(Func<T> work)
        {
            var task = Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            task.ContinueWith(failed => { _ = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return task;
        }

        private static void WaitForStreams(Task[] streams, int milliseconds)
        {
            try { Task.WaitAll(streams, milliseconds); }
            catch (AggregateException) { }
        }

        private static void CloseOutput(Process process)
        {
            try { process.StandardOutput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
            try { process.StandardError.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
        }

        private sealed class OutputLifetime
        {
            internal readonly object Gate = new object();
            internal bool Closed;
            internal void Close() { lock (Gate) Closed = true; }
        }

        private static void TerminateTree(Process process)
        {
            try
            {
                if (process.HasExited) return;
                if (Path.DirectorySeparatorChar == '\\')
                {
                    using (var killer = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                    { if (!killer.WaitForExit(1500)) killer.Kill(); }
                }
                else
                {
                    // Snapshot descendants before killing their parent.
                    // A dedicated reader cannot be starved by the command's blocked pipe readers.
                    using (var listing = Process.Start(new ProcessStartInfo("/bin/ps", "-axo pid=,ppid=") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }))
                    {
                        var read = StartWorker(listing.StandardOutput.ReadToEnd);
                        if (listing.WaitForExit(500) && read.Wait(500))
                        {
                            var descendants = new HashSet<int> { process.Id };
                            var pairs = read.Result.Split('\n').Select(line => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length == 2).ToArray();
                            bool added;
                            do
                            {
                                added = false;
                                foreach (var pair in pairs)
                                    if (int.TryParse(pair[0], out var pid) && int.TryParse(pair[1], out var parent) && descendants.Contains(parent)) added |= descendants.Add(pid);
                            } while (added);
                            // Stop the parent before it can react to child exit by spawning a replacement.
                            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                            foreach (var pid in descendants.Where(pid => pid != process.Id).Reverse())
                                try
                                {
                                    using (var child = Process.GetProcessById(pid)) child.Kill();
                                }
                                catch (ArgumentException) { }
                                catch (InvalidOperationException) { }
                                catch (System.ComponentModel.Win32Exception) { }
                            WaitForUnixProcessesToStop(descendants);
                        }
                        else if (!listing.HasExited) listing.Kill();
                    }
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (AggregateException) { }
            finally
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                    // Kill sends a signal. Closed output pipes do not prove the process has exited.
                    process.WaitForExit(500);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        private static void WaitForUnixProcessesToStop(HashSet<int> owned)
        {
            // Mono does not wait on processes launched by our child. Poll their OS state
            // together under one budget; a zombie has exited even if init has not reaped it.
            var watch = Stopwatch.StartNew();
            var arguments = "-o pid=,stat= -p " + string.Join(",", owned);
            while (watch.ElapsedMilliseconds < 500)
            {
                using (var listing = Process.Start(new ProcessStartInfo("/bin/ps", arguments)
                    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }))
                {
                    var read = StartWorker(listing.StandardOutput.ReadToEnd);
                    var remaining = Math.Max(1, 500 - (int)watch.ElapsedMilliseconds);
                    if (!listing.WaitForExit(remaining) || !read.Wait(Math.Max(1, 500 - (int)watch.ElapsedMilliseconds)))
                    {
                        if (!listing.HasExited) listing.Kill();
                        return;
                    }
                    var running = read.Result.Split('\n')
                        .Select(line => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                        .Any(parts => parts.Length == 2 && int.TryParse(parts[0], out var pid) && owned.Contains(pid)
                            && parts[1][0] != 'Z' && parts[1][0] != 'X');
                    if (!running) return;
                }
                Thread.Sleep(1);
            }
        }

        private string Read(StreamReader stream, OutputLifetime lifetime)
        {
            var output = new StringBuilder();
            var line = new StringBuilder();
            var buffer = new char[2048];
            var discarded = false;
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var character = buffer[i];
                    if (!discarded) line.Append(character);
                    if (line.Length > 65536) { line.Clear(); discarded = true; }
                    if (character != '\n') continue;
                    Append(output, discarded ? "[overlong output line omitted]\n" : line.ToString(), lifetime);
                    line.Clear(); discarded = false;
                }
            }
            if (discarded) Append(output, "[overlong output line omitted]", lifetime);
            else if (line.Length > 0) Append(output, line.ToString(), lifetime);
            return output.ToString();
        }
        private void Append(StringBuilder output, string line, OutputLifetime lifetime)
        {
            var safe = _redact(line);
            lock (lifetime.Gate)
            {
                if (lifetime.Closed) return;
                output.Append(safe);
                if (output.Length > 262144) output.Remove(0, output.Length - 262144);
                _log?.Invoke(safe.TrimEnd('\r', '\n'));
            }
        }
        private static string Quote(string value)
        {
            if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException("Invalid process argument.");
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
        [DllImport("libc", SetLastError = true)] private static extern int chmod(string path, uint mode);
        internal static void CreatePrivateDirectory(string path)
        {
            Directory.CreateDirectory(path);
            if (Path.DirectorySeparatorChar != '\\' && chmod(path, 448) != 0)
            { Directory.Delete(path); throw new IOException("Cannot restrict temporary registry credential directory permissions."); }
        }
    }
}
