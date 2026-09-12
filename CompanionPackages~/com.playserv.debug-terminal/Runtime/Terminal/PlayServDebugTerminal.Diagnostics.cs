using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playserv.Modules;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private const int MaxCapturedErrors = 50;

        private readonly Queue<PlayServError> _capturedErrors = new Queue<PlayServError>();
        private int _keepAlivePingCount;
        private int _keepAlivePongCount;
        private DateTimeOffset? _lastKeepAlivePingAt;
        private DateTimeOffset? _lastKeepAlivePongAt;

        private async Task ExecuteSdkCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "info";
            switch (operation)
            {
                case "info":
                    PrintSdkInfo();
                    return;
                case "modules":
                    PrintSdkModules();
                    return;
                case "latest":
                    await PrintLatestVersionAsync(parts.Count > 2 ? parts[2] : null);
                    return;
                default:
                    AddLog("Usage: sdk <info|modules|latest> [gameId]");
                    return;
            }
        }

        private async Task ExecuteAuthCommandAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count == 1)
            {
                AddLog($"Auth = {GetAuthSummary(includeProviders: true)}");
                return;
            }

            if (string.Equals(parts[1], "refresh", StringComparison.OrdinalIgnoreCase))
            {
                OpenPlayerAccessTokenPrompt();
                return;
            }

            AddLog("Usage: auth [refresh]");
            await Task.CompletedTask;
        }

        private void PrintSdkInfo()
        {
            var settings = PlayServ.Settings;
            if (settings == null)
            {
                AddLog($"SDK {PlayServ.SdkVersion}; state={PlayServ.State}; settings=not configured");
                return;
            }

            AddLog(
                $"SDK {PlayServ.SdkVersion}; state={PlayServ.State}; deployment={settings.DeploymentGameId}; " +
                $"version={settings.GameVersion}; endpoint={settings.BackendServerAddress}");
            AddLog(
                $"multipleConnections={settings.AllowMultipleConnections}; " +
                $"latestLookup={settings.ResolveLatestGameVersionOnConnect}; " +
                $"keepalive={settings.KeepAlivePingIntervalMs}/{settings.KeepAlivePongTimeoutMs}ms");
            AddLog($"Auth = {GetAuthSummary(includeProviders: true)}");
        }

        private void PrintSdkModules()
        {
            var modules = PlayServModuleRegistry.GetRegisteredModuleIds();
            if (modules.Count == 0)
            {
                AddLog("No runtime modules are registered.");
                return;
            }

            AddLog("Runtime modules:");
            foreach (var moduleId in modules)
            {
                AddLog($"- {moduleId}: " +
                       (PlayServModuleRegistry.IsSelected(moduleId) ? "selected" : "not selected"));
            }
        }

        private async Task PrintLatestVersionAsync(string deploymentId)
        {
            deploymentId = string.IsNullOrWhiteSpace(deploymentId)
                ? PlayServ.Settings?.DeploymentGameId
                : deploymentId.Trim();
            if (string.IsNullOrWhiteSpace(deploymentId))
            {
                AddLog("Deployment ID is required. Usage: sdk latest [deploymentId]");
                return;
            }

            var version = await PlayServ.GetLatestVersionAsync(deploymentId);
            _status = $"Latest version for deployment '{deploymentId}' is '{version}'";
            AddLog(_status);
        }

        private void ExecuteKeepAliveCommand(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "status":
                    AddLog(
                        $"Keepalive ping={_keepAlivePingCount} last={FormatTime(_lastKeepAlivePingAt)}; " +
                        $"pong={_keepAlivePongCount} last={FormatTime(_lastKeepAlivePongAt)}");
                    return;
                case "reset":
                    _keepAlivePingCount = 0;
                    _keepAlivePongCount = 0;
                    _lastKeepAlivePingAt = null;
                    _lastKeepAlivePongAt = null;
                    AddLog("Keepalive counters reset.");
                    return;
                default:
                    AddLog("Usage: keepalive <status|reset>");
                    return;
            }
        }

        private void ExecuteErrorsCommand(IReadOnlyList<string> parts)
        {
            if (parts.Count > 1 && string.Equals(parts[1], "clear", StringComparison.OrdinalIgnoreCase))
            {
                _capturedErrors.Clear();
                AddLog("Captured SDK errors cleared.");
                return;
            }

            var count = 10;
            if (parts.Count > 1 && (!int.TryParse(parts[1], out count) || count < 1))
            {
                AddLog("Usage: errors [positive count] | errors clear");
                return;
            }

            var errors = _capturedErrors.ToArray();
            if (errors.Length == 0)
            {
                AddLog("No SDK errors captured.");
                return;
            }

            AddLog($"Last {Math.Min(count, errors.Length)} SDK error(s):");
            foreach (var error in errors.Skip(Math.Max(0, errors.Length - count)))
                AddLog(FormatError(error));
        }

        private void OnPlayServError(PlayServError error)
        {
            if (error == null || !error.IsError)
                return;

            _capturedErrors.Enqueue(error);
            while (_capturedErrors.Count > MaxCapturedErrors)
                _capturedErrors.Dequeue();

            _status = $"SDK error: {error.Code}";
            AddLog(FormatError(error));
        }

        private void OnKeepAlivePingSent()
        {
            _keepAlivePingCount++;
            _lastKeepAlivePingAt = DateTimeOffset.UtcNow;
        }

        private void OnKeepAlivePongReceived()
        {
            _keepAlivePongCount++;
            _lastKeepAlivePongAt = DateTimeOffset.UtcNow;
        }

        internal static string FormatError(PlayServError error)
        {
            if (error == null)
                return "none";

            var http = error.HttpStatus.HasValue ? $", http={error.HttpStatus.Value}" : string.Empty;
            var transport = error.TransportCode.HasValue ? $", transport={error.TransportCode.Value}" : string.Empty;
            return $"{error.Code} source={error.SourceCode}{http}{transport}, retryable={error.Retryable}: {error.Message}";
        }

        private static string FormatTime(DateTimeOffset? value) =>
            value.HasValue ? value.Value.ToLocalTime().ToString("HH:mm:ss") : "never";
    }
}
