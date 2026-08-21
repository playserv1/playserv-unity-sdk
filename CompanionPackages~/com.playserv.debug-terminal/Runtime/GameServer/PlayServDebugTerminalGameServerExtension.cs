using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.GameServer;
using Playserv.Proxy.Common;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal.GameServer
{
    internal sealed partial class PlayServDebugTerminalGameServerExtension : IDebugTerminalCommandExtension
    {
        private readonly PlayServDebugTerminal _terminal;
        private readonly Dictionary<string, PlayServGameRoomHandle> _rooms =
            new Dictionary<string, PlayServGameRoomHandle>(StringComparer.Ordinal);
        private CancellationTokenSource _activeCancellation;
        private string _activeOperation = "none";
        private DebugTerminalMemoryServerKeyProvider _memoryKeyProvider;
        private PlayServGameServerPlayerContext _actingPlayer;
        private bool _ownsRealtime;
        private bool _disposed;

        internal PlayServDebugTerminalGameServerExtension(PlayServDebugTerminal terminal)
        {
            _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
            PlayServGameServer.Realtime.OnError += OnRealtimeError;
        }

        public async Task ExecuteServerCommandAsync(IReadOnlyList<string> parts)
        {
            ThrowIfDisposed();
            var area = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            try
            {
                switch (area)
                {
                    case "configure":
                        await ExecuteConfigureAsync(parts);
                        return;
                    case "status":
                        PrintStatus();
                        return;
                    case "cancel":
                        CancelActiveOperation(false);
                        return;
                    case "shutdown":
                        await RunOperationAsync("shutdown", ShutdownAsync);
                        return;
                    case "realtime":
                        await ExecuteRealtimeAsync(parts);
                        return;
                    case "room":
                        await ExecuteRoomAsync(parts);
                        return;
                    case "match":
                        await ExecuteServerMatchAsync(parts);
                        return;
                    case "reservation":
                        ExecuteReservation(parts);
                        return;
                    case "player":
                        await ExecutePlayerAsync(parts);
                        return;
                    case "jwt":
                        ExecuteJwt(parts);
                        return;
                    case "acting":
                        await ExecuteActingAsync(parts);
                        return;
                    case "code":
                        await ExecuteServerCodeAsync(parts);
                        return;
                    case "analytics":
                        await ExecuteServerAnalyticsAsync(parts);
                        return;
                    case "catalog":
                        await ExecuteServerCatalogAsync(parts);
                        return;
                    case "storefront":
                        await ExecuteServerStorefrontAsync(parts);
                        return;
                    default:
                        Log("Usage: server <configure|status|cancel|shutdown|realtime|room|match|reservation|player|jwt|acting|code|analytics|catalog|storefront> ...");
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                Log("Server command canceled.");
            }
            catch (PlayServGameServerException exception)
            {
                ReportError("Server command failed", exception.UnifiedError);
            }
            catch (Exception exception)
            {
                SetStatus("Server command failed");
                Log($"Server command failed: {exception.Message}");
            }
        }

        public async Task ExecuteRecordCallerCommandAsync(IReadOnlyList<string> parts)
        {
            ThrowIfDisposed();
            if (parts.Count < 3)
            {
                Log($"Records caller: {_terminal.RecordCaller}. Usage: record caller <client|server|acting>");
                return;
            }

            var caller = parts[2].ToLowerInvariant();
            if (caller == "client")
            {
                await _terminal.SwitchRecordCallerAsync("client", null, null, null);
                return;
            }
            if (!PlayServGameServer.IsConfigured)
            {
                Log("Configure the game-server facade first.");
                return;
            }
            if (caller == "server")
            {
                await _terminal.SwitchRecordCallerAsync(
                    "server",
                    () => PlayServGameServer.Records<DebugTerminalPlayerDto>("Player"),
                    GetServerTablesAsync,
                    GetServerTableAsync);
                WarnIfRealtimeUnavailable();
                return;
            }
            if (caller == "acting")
            {
                if (_actingPlayer == null)
                {
                    Log("Set an acting-player JWT first with 'server acting set'.");
                    return;
                }
                await _terminal.SwitchRecordCallerAsync(
                    "acting",
                    () => _actingPlayer.Records<DebugTerminalPlayerDto>("Player"),
                    GetServerTablesAsync,
                    GetServerTableAsync);
                WarnIfRealtimeUnavailable();
                return;
            }

            Log("Usage: record caller <client|server|acting>");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            PlayServGameServer.Realtime.OnError -= OnRealtimeError;
            CancelActiveOperation(true);
            foreach (var room in _rooms.Values)
                room.Dispose();
            _rooms.Clear();
            if (_ownsRealtime)
                Observe(PlayServGameServer.Realtime.DisconnectAsync());
            _ownsRealtime = false;
            _actingPlayer = null;
            _memoryKeyProvider?.Dispose();
            _memoryKeyProvider = null;
        }

        private async Task ExecuteConfigureAsync(IReadOnlyList<string> parts)
        {
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    2,
                    new[] { "backend", "heartbeat-sec", "timeout-sec" },
                    new[] { "prompt" },
                    out var arguments,
                    out var error))
            {
                Log(error);
                return;
            }
            if (arguments.Positionals.Count != 0 ||
                !arguments.TryGetInt("heartbeat-sec", 5, 1, 10, out var heartbeat, out error) ||
                !arguments.TryGetInt("timeout-sec", 10, 1, 3600, out var timeout, out error))
            {
                Log(error ?? "Usage: server configure [--backend url] [--heartbeat-sec 1-10] [--timeout-sec n] [--prompt]");
                return;
            }

            var backend = ResolveBackend(arguments.Get("backend"));
            if (arguments.Has("prompt"))
            {
                _terminal.RequestSecureSecret(
                    "Configure PlayServ Game Server",
                    "Server Key",
                    async secret =>
                    {
                        if (_terminal.RecordCaller != "client")
                            await _terminal.SwitchRecordCallerAsync("client", null, null, null);
                        var provider = new DebugTerminalMemoryServerKeyProvider(secret);
                        try
                        {
                            Configure(backend, heartbeat, timeout, provider);
                            var previous = _memoryKeyProvider;
                            _memoryKeyProvider = provider;
                            previous?.Dispose();
                        }
                        catch
                        {
                            provider.Dispose();
                            throw;
                        }
                    });
                return;
            }

            if (_terminal.RecordCaller != "client")
                await _terminal.SwitchRecordCallerAsync("client", null, null, null);
            Configure(backend, heartbeat, timeout, null);
            _memoryKeyProvider?.Dispose();
            _memoryKeyProvider = null;
        }

        private void Configure(
            string backend,
            int heartbeatSeconds,
            int timeoutSeconds,
            IPlayServServerKeyProvider provider)
        {
            PlayServGameServer.Configure(new PlayServGameServerOptions
            {
                BackendServerAddress = backend,
                ServerKeyProvider = provider,
                HeartbeatInterval = TimeSpan.FromSeconds(heartbeatSeconds),
                HttpTimeout = TimeSpan.FromSeconds(timeoutSeconds)
            });
            _actingPlayer = null;
            SetStatus("Game server configured");
            Log(
                $"Game server configured; backend={backend}; heartbeat={heartbeatSeconds}s; " +
                $"timeout={timeoutSeconds}s; keySource={(provider == null ? "environment" : "memory-only prompt")}");
        }

        private void PrintStatus()
        {
            Log(
                $"Game server: configured={PlayServGameServer.IsConfigured}; realtime={PlayServGameServer.Realtime.State}; " +
                $"rooms={_rooms.Count}; actingPlayer={(_actingPlayer == null ? "none" : "set")}; " +
                $"recordCaller={_terminal.RecordCaller}; operation={_activeOperation}");
            foreach (var room in _rooms.Values)
                PrintRoom(room);
        }

        private async Task ExecuteRealtimeAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "status":
                    Log($"Server realtime: state={PlayServGameServer.Realtime.State}; terminalOwned={_ownsRealtime}");
                    return;
                case "disconnect":
                    await RunOperationAsync("realtime disconnect", async token =>
                    {
                        await _terminal.SwitchRecordCallerAsync("client", null, null, null);
                        await PlayServGameServer.Realtime.DisconnectAsync(token);
                        _ownsRealtime = false;
                        Log("Server realtime disconnected.");
                    });
                    return;
                case "connect":
                    if (!DebugTerminalArguments.TryParse(
                            parts,
                            3,
                            new[] { "game-id", "instance-id", "game-version", "keepalive-sec", "keepalive-timeout-sec" },
                            Array.Empty<string>(),
                            out var arguments,
                            out var error) ||
                        arguments.Positionals.Count != 0 ||
                        !arguments.TryGetInt("keepalive-sec", 30, 1, 3600, out var keepalive, out error) ||
                        !arguments.TryGetInt("keepalive-timeout-sec", 10, 1, 3600, out var keepaliveTimeout, out error))
                    {
                        Log(error ?? "Invalid realtime options.");
                        return;
                    }
                    await RunOperationAsync("realtime connect", async token =>
                    {
                        var connected = await PlayServGameServer.Realtime.ConnectAsync(
                            new PlayServGameServerRealtimeOptions
                            {
                                GameId = arguments.Get("game-id"),
                                InstanceId = arguments.Get("instance-id"),
                                GameVersion = arguments.Get("game-version"),
                                KeepAliveInterval = TimeSpan.FromSeconds(keepalive),
                                KeepAliveTimeout = TimeSpan.FromSeconds(keepaliveTimeout)
                            },
                            token);
                        _ownsRealtime = connected;
                        Log($"Server realtime connect completed; connected={connected}; state={PlayServGameServer.Realtime.State}");
                    });
                    return;
                default:
                    Log("Usage: server realtime <connect|disconnect|status> [options]");
                    return;
            }
        }

        private async Task ExecuteActingAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "status":
                    Log($"Acting-player context: {(_actingPlayer == null ? "not set" : "set (memory only)")}");
                    return;
                case "clear":
                    if (_terminal.RecordCaller == "acting")
                        await _terminal.SwitchRecordCallerAsync("client", null, null, null);
                    _actingPlayer = null;
                    Log("Acting-player JWT cleared.");
                    return;
                case "set":
                    _terminal.RequestSecureSecret(
                        "Set Acting Player",
                        "Player JWT",
                        jwt =>
                        {
                            _actingPlayer = PlayServGameServer.AsPlayer(jwt);
                            Log("Acting-player context set in memory.");
                            return Task.CompletedTask;
                        });
                    return;
                default:
                    Log("Usage: server acting <set|clear|status>");
                    return;
            }
        }

        private async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await _terminal.SwitchRecordCallerAsync("client", null, null, null);
            var result = await PlayServGameServer.ShutdownAsync(cancellationToken);
            _rooms.Clear();
            _actingPlayer = null;
            _ownsRealtime = false;
            _memoryKeyProvider?.Dispose();
            _memoryKeyProvider = null;
            Log(
                $"Game server shutdown completed; roomResults={result.Rooms.Count}; " +
                $"analyticsError={(result.AnalyticsError.IsError ? result.AnalyticsError.SourceCode : "none")}");
        }

        private async Task RunOperationAsync(
            string operation,
            Func<CancellationToken, Task> action)
        {
            if (_activeCancellation != null)
            {
                Log($"Another server operation is running: {_activeOperation}.");
                return;
            }

            var cancellation = new CancellationTokenSource();
            _activeCancellation = cancellation;
            _activeOperation = operation;
            SetStatus($"Server: {operation}");
            try
            {
                await action(cancellation.Token);
            }
            finally
            {
                if (ReferenceEquals(_activeCancellation, cancellation))
                {
                    _activeCancellation = null;
                    _activeOperation = "none";
                }
                cancellation.Dispose();
            }
        }

        private void CancelActiveOperation(bool silent)
        {
            if (_activeCancellation == null)
            {
                if (!silent)
                    Log("No server operation is running.");
                return;
            }
            _activeCancellation.Cancel();
            if (!silent)
                Log($"Cancellation requested for server operation '{_activeOperation}'.");
        }

        private Task<IReadOnlyList<PlayServDataTableInfo>> GetServerTablesAsync(
            bool refresh,
            CancellationToken cancellationToken) =>
            refresh
                ? PlayServGameServer.RefreshTablesAsync(cancellationToken)
                : PlayServGameServer.GetTablesAsync(cancellationToken);

        private async Task<PlayServDataTableInfo> GetServerTableAsync(
            string idOrName,
            bool refresh,
            CancellationToken cancellationToken)
        {
            if (refresh)
                await PlayServGameServer.RefreshTablesAsync(cancellationToken);
            return await PlayServGameServer.GetTableAsync(idOrName, cancellationToken);
        }

        private string ResolveBackend(string explicitValue)
        {
            var backend = string.IsNullOrWhiteSpace(explicitValue)
                ? Environment.GetEnvironmentVariable("PLAYSERV_API_URL")
                : explicitValue;
            if (string.IsNullOrWhiteSpace(backend))
                backend = _terminal.CurrentBackendAddress;
            if (string.IsNullOrWhiteSpace(backend))
                throw new InvalidOperationException("Backend address is missing. Use --backend or PLAYSERV_API_URL.");
            return backend.Trim();
        }

        private void OnRealtimeError(PlayServError error) => ReportError("Server realtime error", error);

        private void WarnIfRealtimeUnavailable()
        {
            if (PlayServGameServer.Realtime.State != PlayServState.Online)
            {
                Log(
                    "Server Records HTTP operations are ready; realtime subscribe/watch requires " +
                    "'server realtime connect'.");
            }
        }

        private void ReportError(string prefix, PlayServError error)
        {
            SetStatus(prefix);
            _terminal.CaptureExtensionError(error);
            Log($"{prefix}: {PlayServDebugTerminal.FormatError(error)}");
        }

        private void Log(string message) => _terminal.AddExtensionLog(message);

        private void SetStatus(string status) => _terminal.SetExtensionStatus(status);

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlayServDebugTerminalGameServerExtension));
        }

        private static string RoomKey(string functionSlug, string roomName) =>
            (functionSlug ?? string.Empty).Trim() + "\n" + (roomName ?? string.Empty).Trim();

        private static void Observe(Task task)
        {
            if (task == null)
                return;
            _ = task.ContinueWith(
                ignored => { },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
