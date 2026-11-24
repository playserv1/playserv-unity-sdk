using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    internal sealed class PlayServSchemaToolResult
    {
        public bool Success;
        public int ExitCode;
        public string Output = string.Empty;
        public string Error = string.Empty;
        public PlayServSchemaToolResponse Response;

        public string Message
        {
            get
            {
                if (Response != null && !string.IsNullOrWhiteSpace(Response.message))
                    return Response.message;
                if (!string.IsNullOrWhiteSpace(Error))
                    return Error.Trim();
                return Output.Trim();
            }
        }
    }

    [Serializable]
    internal sealed class PlayServSchemaToolResponse
    {
        public bool success;
        public string command;
        public string version;
        public int protocolVersion;
        public int sourceFileCount;
        public int schemaCount;
        public int pushedSchemaCount;
        public bool generatedFilesChanged;
        public string previousRevision;
        public string revision;
        public bool dryRun;
        public string message;
        public string[] outputs;
        public PlayServSchemaDiagnostic[] diagnostics;
    }

    [Serializable]
    internal sealed class PlayServSchemaDiagnostic
    {
        public string severity;
        public string code;
        public string message;
        public string path;
        public int line;
        public int column;
        public string type;
        public string member;
    }

    [Serializable]
    internal sealed class PlayServSchemaToolWatchState
    {
        public int pid;
        public string projectRoot;
        public string toolVersion;
        public int protocolVersion;
        public string startedAtUtc;
    }

    internal static class PlayServSchemaToolRunner
    {
        private const int CommandTimeoutMilliseconds = 120000;
        private static Process _watchProcess;
        private static string _watchOutput = string.Empty;

        static PlayServSchemaToolRunner()
        {
            EditorApplication.update += PollWatchProcess;
        }

        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string ConfigurationPath =>
            Path.Combine(ProjectRoot, "playserv.schema.json");

        public static string LauncherDirectory =>
            Path.Combine(ProjectRoot, ".playserv", "bin");

        public static string WatchOutput => _watchOutput;

        public static bool IsInstalled =>
            PlayServCompanionPackageManager.TryGetInstalledPackage(
                PlayServSchemaToolPackage.PackageId,
                out _);

        public static bool IsConfigured => File.Exists(ConfigurationPath);

        public static bool IsWatchRunning
        {
            get
            {
                if (IsRunning(_watchProcess))
                    return true;

                return TryResolveWatchProcess(out _);
            }
        }

        public static bool TryResolveTool(
            out string dotnetPath,
            out string toolPath,
            out string error)
        {
            dotnetPath = ResolveBundledDotNet();
            toolPath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(dotnetPath) || !File.Exists(dotnetPath))
            {
                error =
                    "Unity's bundled .NET runtime was not found. Use Unity 2022.3.62f3 or newer.";
                return false;
            }

            if (!PlayServCompanionPackageManager.TryGetInstalledPackage(
                    PlayServSchemaToolPackage.PackageId,
                    out var packageInfo))
            {
                error = $"{PlayServSchemaToolPackage.PackageId} is not installed.";
                return false;
            }

            toolPath = Path.Combine(
                packageInfo.resolvedPath ?? string.Empty,
                PlayServSchemaToolPackage.ToolRelativePath);
            if (!File.Exists(toolPath))
            {
                error = $"Schema Tool executable was not found at {toolPath}.";
                return false;
            }

            return true;
        }

        public static async Task<PlayServSchemaToolResult> RunAsync(string command)
        {
            if (!TryResolveTool(out var dotnetPath, out var toolPath, out var error))
                return Failed(error);

            var arguments =
                $"{Quote(toolPath)} {Quote(command)} --project {Quote(ProjectRoot)} --json";
            var startInfo = CreateStartInfo(dotnetPath, arguments, redirect: true);

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return Failed("Failed to start PlayServ Schema Tool.");

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    var exitTask = Task.Run(() =>
                    {
                        process.WaitForExit();
                        return process.ExitCode;
                    });
                    var completed = await Task.WhenAny(
                        exitTask,
                        Task.Delay(CommandTimeoutMilliseconds));

                    if (completed != exitTask)
                    {
                        TryKill(process);
                        await exitTask;
                        await outputTask;
                        await errorTask;
                        return Failed(
                            $"Schema Tool command '{command}' timed out after 120 seconds.");
                    }

                    var output = await outputTask;
                    var standardError = await errorTask;
                    var exitCode = await exitTask;
                    var response = ParseResponse(output);
                    return new PlayServSchemaToolResult
                    {
                        Success = exitCode == 0 && (response == null || response.success),
                        ExitCode = exitCode,
                        Output = output,
                        Error = standardError,
                        Response = response
                    };
                }
            }
            catch (Exception exception)
            {
                return Failed(exception.GetBaseException().Message);
            }
        }

        internal static PlayServSchemaToolResult RunContractValidation(string dotnet, string tool,
            string projectRoot, string inputsPath, int timeoutMilliseconds = CommandTimeoutMilliseconds)
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = dotnet,
                    Arguments = $"{Quote(tool)} validate-contracts --project {Quote(projectRoot)} --build-inputs {Quote(inputsPath)} --json",
                    WorkingDirectory = projectRoot, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = Process.Start(start))
                {
                    if (process == null) return Failed("Could not start Schema Tool.");
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        TryKill(process);
                        return Failed("Schema Tool contract validation timed out.");
                    }
                    if (!Task.WaitAll(new Task[] { output, error }, timeoutMilliseconds))
                        return Failed("Schema Tool contract validation output timed out.");
                    return ParseContractValidationResult(process.ExitCode, output.Result, error.Result);
                }
            }
            catch (Exception exception) { return Failed(exception.GetBaseException().Message); }
        }

        internal static PlayServSchemaToolResult ParseContractValidationResult(int exitCode, string output, string error)
        {
            var response = ParseResponse(output);
            if (response == null || response.command != "validate-contracts" || response.protocolVersion != 1 ||
                response.diagnostics == null || (response.success && exitCode != 0) ||
                (!response.success && exitCode == 0) || Array.Exists(response.diagnostics, diagnostic =>
                    diagnostic == null || string.IsNullOrWhiteSpace(diagnostic.code) || string.IsNullOrWhiteSpace(diagnostic.message) ||
                    (diagnostic.severity != "error" && diagnostic.severity != "warning" && diagnostic.severity != "info")))
                return Failed("Schema Tool returned an invalid contract-validation response. Ensure core and companion use the same SDK revision. " + error);
            var hasErrors = Array.Exists(response.diagnostics, diagnostic => diagnostic == null || diagnostic.severity == "error");
            if (response.success && hasErrors)
                return Failed("Schema Tool returned inconsistent contract-validation diagnostics.");
            return new PlayServSchemaToolResult
            {
                Success = exitCode == 0 && response.success, ExitCode = exitCode,
                Output = output, Error = error, Response = response
            };
        }

        public static bool StartWatch(out string error)
        {
            error = string.Empty;
            if (IsWatchRunning)
                return true;
            if (!IsConfigured)
            {
                error = "Initialize the schema project before starting Watch.";
                return false;
            }

            if (!TryResolveTool(out var dotnetPath, out var toolPath, out error))
                return false;

            var arguments =
                $"{Quote(toolPath)} watch --project {Quote(ProjectRoot)} --json";
            try
            {
                var process = new Process
                {
                    StartInfo = CreateStartInfo(dotnetPath, arguments, redirect: true)
                };
                process.OutputDataReceived += OnWatchOutput;
                process.ErrorDataReceived += OnWatchOutput;
                if (!process.Start())
                {
                    process.Dispose();
                    error = "Failed to start PlayServ Schema Tool watcher.";
                    return false;
                }

                _watchProcess = process;
                _watchOutput = "Schema watcher is starting.";
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetBaseException().Message;
                return false;
            }
        }

        public static bool StopWatch(out string error)
        {
            error = string.Empty;
            var process = IsRunning(_watchProcess)
                ? _watchProcess
                : TryResolveWatchProcess(out var persistedProcess)
                    ? persistedProcess
                    : null;

            if (process == null)
            {
                DeleteStaleWatchState();
                _watchOutput = "Schema watcher is stopped.";
                return true;
            }

            try
            {
                TryKill(process);
                process.WaitForExit(3000);
                DeleteStaleWatchState();
                _watchOutput = "Schema watcher is stopped.";
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetBaseException().Message;
                return false;
            }
            finally
            {
                if (!ReferenceEquals(process, _watchProcess))
                    process.Dispose();
                DisposeWatchProcess();
            }
        }

        public static bool CreateIdeLaunchers(out string launcherPath, out string error)
        {
            launcherPath = string.Empty;
            error = string.Empty;
            if (!TryResolveTool(out var dotnetPath, out var toolPath, out error))
                return false;

            try
            {
                Directory.CreateDirectory(LauncherDirectory);
                EnsureProjectToolIgnore();
                var shellPath = Path.Combine(LauncherDirectory, "playserv-schema");
                var commandPath = Path.Combine(LauncherDirectory, "playserv-schema.cmd");
                File.WriteAllText(
                    shellPath,
                    "#!/usr/bin/env sh\n" +
                    $"exec {QuoteForShell(dotnetPath)} {QuoteForShell(toolPath)} \"$@\"\n",
                    new UTF8Encoding(false));
                File.WriteAllText(
                    commandPath,
                    "@echo off\r\n" +
                    $"\"{dotnetPath}\" \"{toolPath}\" %*\r\n",
                    new UTF8Encoding(false));

                if (Application.platform != RuntimePlatform.WindowsEditor)
                    MakeExecutable(shellPath);

                launcherPath = Application.platform == RuntimePlatform.WindowsEditor
                    ? commandPath
                    : shellPath;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetBaseException().Message;
                return false;
            }
        }

        internal static string ResolveBundledDotNet()
        {
            return ResolveBundledDotNet(
                EditorApplication.applicationContentsPath,
                Application.platform);
        }

        internal static string ResolveBundledDotNet(
            string applicationContentsPath,
            RuntimePlatform platform)
        {
            var fileName = platform == RuntimePlatform.WindowsEditor
                ? "dotnet.exe"
                : "dotnet";
            var candidates = BuildBundledDotNetCandidates(
                applicationContentsPath,
                fileName);
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        internal static string[] BuildBundledDotNetCandidates(
            string applicationContentsPath,
            string fileName)
        {
            var contents = applicationContentsPath ?? string.Empty;
            return new[]
            {
                Path.Combine(contents, "NetCoreRuntime", fileName),
                Path.Combine(contents, "Resources", "Scripting", "NetCoreRuntime", fileName),
                Path.Combine(contents, "Resources", "Scripting", "DotNetSdk", fileName)
            };
        }

        internal static string ResolveToolPath(PackageManagerPackageInfo packageInfo)
        {
            return packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath)
                ? string.Empty
                : Path.Combine(
                    packageInfo.resolvedPath,
                    PlayServSchemaToolPackage.ToolRelativePath);
        }

        private static ProcessStartInfo CreateStartInfo(
            string executable,
            string arguments,
            bool redirect)
        {
            return new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect
            };
        }

        private static PlayServSchemaToolResponse ParseResponse(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            try
            {
                return JsonUtility.FromJson<PlayServSchemaToolResponse>(output);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryResolveWatchProcess(out Process process)
        {
            process = null;
            var statePath = GetWatchStatePath();
            if (!File.Exists(statePath))
                return false;

            try
            {
                var state = JsonUtility.FromJson<PlayServSchemaToolWatchState>(
                    File.ReadAllText(statePath));
                if (state == null ||
                    state.pid <= 0 ||
                    !string.Equals(
                        Path.GetFullPath(state.projectRoot ?? string.Empty),
                        ProjectRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                process = Process.GetProcessById(state.pid);
                if (IsExpectedWatchProcess(process, state))
                    return true;

                process.Dispose();
                process = null;
            }
            catch
            {
                process = null;
            }

            DeleteStaleWatchState();
            return false;
        }

        private static string GetWatchStatePath()
        {
            return Path.Combine(ProjectRoot, ".playserv", "watch-state.json");
        }

        private static void DeleteStaleWatchState()
        {
            try
            {
                var path = GetWatchStatePath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A live watcher may still own its state file.
            }
        }

        private static bool IsRunning(Process process)
        {
            if (process == null)
                return false;

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExpectedWatchProcess(
            Process process,
            PlayServSchemaToolWatchState state)
        {
            if (!IsRunning(process) ||
                state == null ||
                string.IsNullOrWhiteSpace(state.startedAtUtc) ||
                !DateTime.TryParse(
                    state.startedAtUtc,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var startedAt))
            {
                return false;
            }

            try
            {
                var processName = process.ProcessName ?? string.Empty;
                var startDelta = (
                    process.StartTime.ToUniversalTime() -
                    startedAt.ToUniversalTime()).Duration();
                return processName.IndexOf("dotnet", StringComparison.OrdinalIgnoreCase) >= 0 &&
                       startDelta < TimeSpan.FromSeconds(10);
            }
            catch
            {
                return false;
            }
        }

        private static void TryKill(Process process)
        {
            if (IsRunning(process))
                process.Kill();
        }

        private static void OnWatchOutput(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;

            _watchOutput = args.Data.Trim();
        }

        private static void PollWatchProcess()
        {
            if (_watchProcess == null || IsRunning(_watchProcess))
                return;

            _watchOutput = "Schema watcher stopped.";
            DisposeWatchProcess();
            RepaintAllViews();
        }

        private static void DisposeWatchProcess()
        {
            if (_watchProcess == null)
                return;

            _watchProcess.OutputDataReceived -= OnWatchOutput;
            _watchProcess.ErrorDataReceived -= OnWatchOutput;
            _watchProcess.Dispose();
            _watchProcess = null;
        }

        private static void RepaintAllViews()
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static void MakeExecutable(string path)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x {Quote(path)}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(startInfo))
                process?.WaitForExit();
        }

        private static void EnsureProjectToolIgnore()
        {
            var ignorePath = Path.Combine(ProjectRoot, ".playserv", ".gitignore");
            if (File.Exists(ignorePath))
                return;

            File.WriteAllText(
                ignorePath,
                "bin/\n*.lock\n*.pid\nwatch-state.json\n",
                new UTF8Encoding(false));
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\"", "\\\"") + "\"";
        }

        private static string QuoteForShell(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
        }

        private static PlayServSchemaToolResult Failed(string error)
        {
            return new PlayServSchemaToolResult
            {
                Success = false,
                ExitCode = -1,
                Error = error ?? "Unknown Schema Tool error."
            };
        }
    }
}
