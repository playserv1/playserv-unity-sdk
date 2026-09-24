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
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                var start = new ProcessStartInfo(_executable, string.Join(" ", args.Select(Quote)))
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
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (var process = new Process { StartInfo = start })
                {
                    deadline.CancelAfter(timeout);
                    process.Start();
                    using (deadline.Token.Register(() =>
                    {
                        TerminateTree(process);
                        process.StandardOutput.Close();
                        process.StandardError.Close();
                    }))
                    {
                        var stdout = ReadAsync(process.StandardOutput);
                        var stderr = ReadAsync(process.StandardError);
                        try
                        {
                            if (input != null) await process.StandardInput.WriteAsync(input);
                            process.StandardInput.Close();
                            var output = await stdout;
                            var errors = await stderr;
                            process.WaitForExit();
                            deadline.Token.ThrowIfCancellationRequested();
                            return new ServerImageProcessResult { ExitCode = process.ExitCode, Output = output + errors };
                        }
                        catch (Exception) when (deadline.IsCancellationRequested)
                        {
                            ct.ThrowIfCancellationRequested();
                            throw new TimeoutException("Docker operation exceeded its time budget.");
                        }
                        finally
                        {
                            TerminateTree(process);
                        }
                    }
                }
            }, ct);
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
                    // Mono does not expose Kill(entireProcessTree). Snapshot descendants before killing their parent.
                    using (var listing = Process.Start(new ProcessStartInfo("/bin/ps", "-axo pid=,ppid=") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }))
                    {
                        var read = listing.StandardOutput.ReadToEndAsync();
                        if (listing.WaitForExit(1000) && read.Wait(1000))
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
                            foreach (var pid in descendants.Where(pid => pid != process.Id).Reverse())
                                try { using (var child = Process.GetProcessById(pid)) child.Kill(); } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                        }
                        else if (!listing.HasExited) listing.Kill();
                    }
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        }

        private async Task<string> ReadAsync(StreamReader stream)
        {
            var output = new StringBuilder();
            var line = new StringBuilder();
            var buffer = new char[2048];
            var discarded = false;
            int count;
            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var character = buffer[i];
                    if (!discarded) line.Append(character);
                    if (line.Length > 65536) { line.Clear(); discarded = true; }
                    if (character != '\n') continue;
                    Append(output, discarded ? "[overlong output line omitted]\n" : line.ToString());
                    line.Clear(); discarded = false;
                }
            }
            if (discarded) Append(output, "[overlong output line omitted]");
            else if (line.Length > 0) Append(output, line.ToString());
            return output.ToString();
        }
        private void Append(StringBuilder output, string line)
        {
            var safe = _redact(line);
            output.Append(safe);
            if (output.Length > 262144) output.Remove(0, output.Length - 262144);
            _log?.Invoke(safe.TrimEnd('\r', '\n'));
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
