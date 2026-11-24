using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.GameServer
{
    internal sealed class PlayServEngineLogCapture : IDisposable
    {
        private readonly object _gate = new object();
        private readonly PlayServGameServerContext _context;
        private readonly PlayServGameServerLogs _logs;
        private readonly PlayServEngineLogOptions _options;
        private readonly SynchronizationContext _unity;
        private readonly int _thread;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private Task _loop;
        private bool _started, _stopped;
        private double _windowStart = double.NaN;
        private int _inWindow;
        private long _accepted, _filtered, _rateLimited, _queueRejected, _failed;
        private PlayServError _error = PlayServError.None;

        internal PlayServEngineLogCapture(PlayServGameServerContext context, PlayServGameServerLogs logs)
        {
            _context = context; _logs = logs; _options = context.EngineLogs;
            _unity = SynchronizationContext.Current; _thread = Thread.CurrentThread.ManagedThreadId;
        }

        internal static void ValidateConfigurationThread(PlayServEngineLogOptions options)
        {
            if (!options.Enabled) return;
            try { PlayServGameServer.EnsureSupportedBuildForServices(); }
            catch (PlatformNotSupportedException) { return; }
            if (SynchronizationContext.Current?.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
                throw new InvalidOperationException("Configure engine log capture on Unity's main thread.");
        }

        internal PlayServEngineLogDiagnostics Snapshot()
        {
            lock (_gate) return new PlayServEngineLogDiagnostics(_started && !_stopped, _accepted, _filtered,
                _rateLimited, _queueRejected, _failed, _error);
        }

        internal void Start()
        {
            lock (_gate)
            {
                if (_started || _stopped) return;
                _started = true;
                Application.logMessageReceivedThreaded += OnLog;
                _loop = Task.Run(RunAsync);
            }
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (EngineLogSuppression.Active) return;
            using var suppression = EngineLogSuppression.Enter();
            lock (_gate)
            {
                if (_stopped || _context.ShuttingDown) return;
                var level = type == LogType.Log ? PlayServServerLogLevel.Info :
                    type == LogType.Warning ? PlayServServerLogLevel.Warning : PlayServServerLogLevel.Error;
                if (level < _options.MinimumLevel || string.IsNullOrEmpty(message)) { _filtered++; return; }
                var now = _context.MonotonicSeconds();
                if (double.IsNaN(_windowStart) || now - _windowStart >= _options.RateWindow.TotalSeconds)
                { _windowStart = now; _inWindow = 0; }
                if (_inWindow >= _options.MaxEntriesPerWindow) { _rateLimited++; return; }
                _inWindow++;
                try
                {
                    // No raw text is posted to Unity's dispatcher or retained in a second queue.
                    var text = Cap(PlayServGameServerLogs.EngineText(message), _options.MaxTextBytes);
                    var stack = _options.IncludeStackTrace && level == PlayServServerLogLevel.Error ?
                        Cap(PlayServGameServerLogs.EngineText(stackTrace), _options.MaxTextBytes - Encoding.UTF8.GetByteCount(text)) : null;
                    if (_logs.TryWriteEngine(_context, text, level, string.IsNullOrEmpty(stack) ? null : stack)) _accepted++;
                    else _queueRejected++;
                }
                catch { _failed++; _error = Failure("engine_log_capture_failed"); }
            }
        }

        private async Task RunAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    await _context.Delay(_options.FlushInterval, _stop.Token).ConfigureAwait(false);
                    _stop.Token.ThrowIfCancellationRequested();
                    if (_logs.PendingCount == 0) continue;
                    var result = await _logs.FlushForContextAsync(_context, _stop.Token).ConfigureAwait(false);
                    lock (_gate) _error = result.Error;
                }
            }
            catch (OperationCanceledException) { }
            catch { lock (_gate) { _failed++; _error = Failure("engine_log_forwarding_failed"); } }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_stopped) return;
                _stopped = true;
                if (_started)
                {
                    if (Thread.CurrentThread.ManagedThreadId == _thread) Application.logMessageReceivedThreaded -= OnLog;
                    else _unity.Post(_ => Application.logMessageReceivedThreaded -= OnLog, null);
                }
                _stop.Cancel();
                if (_loop == null || _loop.IsCompleted) _stop.Dispose();
                else _ = _loop.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
            }
        }

        private static PlayServError Failure(string code) => new PlayServError(PlayServErrorCode.Transport, code,
            "Engine log forwarding could not complete. Inspect the local counters and uplink state.");

        private static string Cap(string text, int limit)
        {
            if (limit <= 0 || string.IsNullOrEmpty(text)) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length <= limit) return text;
            var length = Math.Max(0, limit - 3);
            while (length > 0 && (bytes[length] & 0xC0) == 0x80) length--;
            return Encoding.UTF8.GetString(bytes, 0, length) + (limit >= 3 ? "..." : "");
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorCleanup()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= PlayServGameServer.Logs.StopEngineCapture;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += PlayServGameServer.Logs.StopEngineCapture;
            UnityEditor.EditorApplication.playModeStateChanged -= OnEditorStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnEditorStateChanged;
        }
        internal static void OnEditorStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                PlayServGameServer.Logs.StopEngineCapture();
        }
#endif
    }

    // Async-flow scope: suppress this send's diagnostics without losing unrelated game-thread logs.
    internal static class EngineLogSuppression
    {
        private static readonly AsyncLocal<int> Depth = new AsyncLocal<int>();
        internal static bool Active => Depth.Value != 0;
        internal static IDisposable Enter() => new Scope();
        private sealed class Scope : IDisposable
        {
            private readonly int _previous = Depth.Value;
            internal Scope() { Depth.Value = _previous + 1; }
            public void Dispose() { Depth.Value = _previous; }
        }
    }
}
