using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    public sealed class KeepAliveManager : IDisposable
    {
        private const string KeepAliveLogPrefix = "[PlayServ][KeepAlive]";
        private const string KeepAliveEventType = "KeepAlive";
        private const string EmptyPayloadJson = "{}";
        private const int DefaultKeepAliveIntervalMs = 30000;

        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly object _lock = new();
        private readonly HashSet<Task> _backgroundTasks = new HashSet<Task>();

        private CancellationTokenSource _cts;
        private IDisposable _legacyPingSubscription;
        private IDisposable _eventKeepAliveSubscription;
        private Task _sendLoop;
        private Task _monitorLoop;
        private bool _isRunning;
        private bool _isDisposed;
        private int _timeoutRaised;
        private int _heartbeatSequence;
        private long _startedAtMs;
        private long _lastClientKeepAliveAtMs;
        private long _lastServerKeepAliveAtMs;

        public int PingIntervalMs { get; set; } = 30000;
        public int PongTimeoutMs { get; set; } = 10000;

        public event Action OnTimeout;
        public event Action OnPingSent;
        public event Action PongReceived;

        public KeepAliveManager(ITransport transport, ILogger logger)
        {
            _transport = transport;
            _logger = logger;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(KeepAliveManager));

                if (_isRunning)
                    return;

                _isRunning = true;
                _cts = new CancellationTokenSource();
                var cancellationToken = _cts.Token;
                _timeoutRaised = 0;
                _heartbeatSequence = 0;
                Interlocked.Exchange(ref _lastClientKeepAliveAtMs, 0);
                Interlocked.Exchange(ref _lastServerKeepAliveAtMs, 0);
                Interlocked.Exchange(ref _startedAtMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                _legacyPingSubscription = _transport.OnReceive<KeepAliveRequest>()
                    .Subscribe(OnLegacyPingReceived);

                _eventKeepAliveSubscription = _transport.OnReceive<EventMessage>()
                    .Subscribe(OnEventMessageReceived);

#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL is single-threaded: Task.Run would detach the continuation from
                // the Unity SynchronizationContext and _transport.Send (which ends up in a
                // [DllImport("__Internal")] call) must run on the main thread. Keep loops
                // on the main thread scheduler via direct invocation + Task.Yield-based delays.
                _sendLoop = SendLoopAsync(cancellationToken);
                _monitorLoop = MonitorLoopAsync(cancellationToken);
#else
                _sendLoop = Task.Run(() => SendLoopAsync(cancellationToken));
                _monitorLoop = Task.Run(() => MonitorLoopAsync(cancellationToken));
#endif
                TrackBackgroundTaskLocked(_sendLoop);
                TrackBackgroundTaskLocked(_monitorLoop);
                LogKeepAlive(
                    $"{KeepAliveLogPrefix} manager started. twait={ResolveKeepAliveIntervalMs()}ms, " +
                    $"waitWindow={ResolveKeepAliveWaitWindowMs()}ms");
            }
        }

        public void Stop()
        {
            CancellationTokenSource cancellation;
            IDisposable legacyPingSubscription;
            IDisposable eventKeepAliveSubscription;

            lock (_lock)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
                cancellation = _cts;
                legacyPingSubscription = _legacyPingSubscription;
                eventKeepAliveSubscription = _eventKeepAliveSubscription;
                _cts = null;
                _legacyPingSubscription = null;
                _eventKeepAliveSubscription = null;
                _sendLoop = null;
                _monitorLoop = null;
            }

            cancellation?.Cancel();
            legacyPingSubscription?.Dispose();
            eventKeepAliveSubscription?.Dispose();
            cancellation?.Dispose();

            LogKeepAlive(
                $"{KeepAliveLogPrefix} manager stopped. lastClientKeepAliveAt={FormatTimestamp(Interlocked.Read(ref _lastClientKeepAliveAtMs))}, " +
                $"lastServerKeepAliveAt={FormatTimestamp(Interlocked.Read(ref _lastServerKeepAliveAtMs))}");
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            var firstPingDelayMs = Math.Min(ResolveKeepAliveSendIntervalMs(), 5000);
            LogKeepAlive(
                $"{KeepAliveLogPrefix} heartbeat loop started. first KeepAlive in {firstPingDelayMs}ms");

            var isFirstPing = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var delayMs = isFirstPing ? firstPingDelayMs : ResolveKeepAliveSendIntervalMs();
                    isFirstPing = false;
                    await DelayWithCancellationAsync(delayMs, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var success = await SendKeepAliveAsync("periodic", cancellationToken, triggerTimeoutOnFailure: true);
                    if (!success)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    LogKeepAlive($"{KeepAliveLogPrefix} heartbeat loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    LogKeepAliveError($"{KeepAliveLogPrefix} send loop error: {ex.Message}");
                    TriggerTimeout("send loop failed", cancellationToken);
                    break;
                }
            }
        }

        private async Task MonitorLoopAsync(CancellationToken cancellationToken)
        {
            var monitorTickMs = ResolveMonitorTickMs();
            LogKeepAlive(
                $"{KeepAliveLogPrefix} watchdog loop started. checkEvery={monitorTickMs}ms");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await DelayWithCancellationAsync(monitorTickMs, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var waitWindowMs = ResolveKeepAliveWaitWindowMs();
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var lastServerKeepAliveAtMs = Interlocked.Read(ref _lastServerKeepAliveAtMs);
                    var sinceLastServerKeepAliveMs = lastServerKeepAliveAtMs > 0
                        ? Math.Max(0, nowMs - lastServerKeepAliveAtMs)
                        : Math.Max(0, nowMs - Interlocked.Read(ref _startedAtMs));

                    if (sinceLastServerKeepAliveMs <= waitWindowMs)
                        continue;

                    LogKeepAliveWarning(
                        $"{KeepAliveLogPrefix} server KeepAlive not received in time. " +
                        $"waited={waitWindowMs}ms, " +
                        $"silence={sinceLastServerKeepAliveMs}ms, " +
                        $"lastClientKeepAliveAt={FormatTimestamp(Interlocked.Read(ref _lastClientKeepAliveAtMs))}, " +
                        $"lastServerKeepAliveAt={FormatTimestamp(lastServerKeepAliveAtMs)}");
                    TriggerTimeout("server keepalive timeout", cancellationToken);
                    break;
                }
                catch (OperationCanceledException)
                {
                    LogKeepAlive($"{KeepAliveLogPrefix} watchdog loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    LogKeepAliveError($"{KeepAliveLogPrefix} watchdog loop error: {ex.Message}");
                    TriggerTimeout("watchdog loop failed", cancellationToken);
                    break;
                }
            }
        }

        private async Task<bool> SendKeepAliveAsync(
            string source,
            CancellationToken cancellationToken,
            bool triggerTimeoutOnFailure)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sequence = Interlocked.Increment(ref _heartbeatSequence);
            Interlocked.Exchange(ref _lastClientKeepAliveAtMs, nowMs);

            var keepAliveCommand = new EventMessage(KeepAliveEventType, EmptyPayloadJson);
            LogKeepAlive(
                $"{KeepAliveLogPrefix} -> heartbeat#{sequence} send attempt. source={source}, event={KeepAliveEventType}, ts={FormatTimestamp(nowMs)}");

            try
            {
                await _transport.Send(keepAliveCommand);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogKeepAliveWarning($"{KeepAliveLogPrefix} heartbeat#{sequence} send failed. source={source}, error={ex.Message}");
                if (triggerTimeoutOnFailure)
                    TriggerTimeout("failed to send keepalive", cancellationToken);
                return false;
            }

            if (!IsCurrentRun(cancellationToken))
                return false;

            OnPingSent?.Invoke();
            LogKeepAlive(
                $"{KeepAliveLogPrefix} -> heartbeat#{sequence} sent. source={source}, sendEvery={ResolveKeepAliveSendIntervalMs()}ms, twait={ResolveKeepAliveIntervalMs()}ms");
            return true;
        }

        private void OnEventMessageReceived(EventMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.EventType))
                return;

            if (!IsKeepAliveEventType(message.EventType))
                return;

            if (!TryGetRunningToken(out var cancellationToken))
                return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var previousServerKeepAliveAtMs = Interlocked.Exchange(ref _lastServerKeepAliveAtMs, nowMs);
            var elapsedMs = previousServerKeepAliveAtMs > 0
                ? Math.Max(0, nowMs - previousServerKeepAliveAtMs)
                : -1;

            if (!IsCurrentRun(cancellationToken))
                return;

            PongReceived?.Invoke();
            if (elapsedMs >= 0)
            {
                LogKeepAlive(
                    $"{KeepAliveLogPrefix} <- server event '{message.EventType}' received. delta={elapsedMs}ms");
            }
            else
            {
                LogKeepAlive(
                    $"{KeepAliveLogPrefix} <- first server event '{message.EventType}' received. at={FormatTimestamp(nowMs)}");
            }

            TrackBackgroundTask(RespondToServerKeepAliveAsync(cancellationToken));
        }

        private static bool IsKeepAliveEventType(string eventTypeName)
        {
            return string.Equals(eventTypeName, KeepAliveEventType, StringComparison.OrdinalIgnoreCase);
        }

        private int ResolveKeepAliveIntervalMs()
        {
            return PingIntervalMs > 0 ? PingIntervalMs : DefaultKeepAliveIntervalMs;
        }

        private int ResolveKeepAliveSendIntervalMs()
        {
            var intervalMs = ResolveKeepAliveIntervalMs();
            var safetyMarginMs = Math.Min(5000, Math.Max(1000, intervalMs / 6));
            return Math.Max(1000, intervalMs - safetyMarginMs);
        }

        private int ResolveKeepAliveWaitWindowMs()
        {
            var intervalMs = ResolveKeepAliveIntervalMs();
            var graceMs = PongTimeoutMs > 0
                ? PongTimeoutMs
                : Math.Max(1000, intervalMs / 3);

            return intervalMs + graceMs;
        }

        private int ResolveMonitorTickMs()
        {
            var waitWindowMs = ResolveKeepAliveWaitWindowMs();
            var tickMs = waitWindowMs / 4;
            tickMs = Math.Min(tickMs, 2000);
            return Math.Max(tickMs, 500);
        }

        private void TriggerTimeout(string reason, CancellationToken cancellationToken)
        {
            if (!IsCurrentRun(cancellationToken))
                return;

            if (Interlocked.Exchange(ref _timeoutRaised, 1) != 0)
                return;

            LogKeepAliveWarning($"{KeepAliveLogPrefix} timeout triggered: {reason}");
            OnTimeout?.Invoke();
        }

        private void OnLegacyPingReceived(KeepAliveRequest request)
        {
            lock (_lock)
            {
                if (!_isRunning || _cts == null || _cts.IsCancellationRequested)
                    return;

                TrackBackgroundTaskLocked(RespondToLegacyPingAsync(request, _cts.Token));
            }
        }

        private async Task RespondToLegacyPingAsync(
            KeepAliveRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LogKeepAlive(
                    $"{KeepAliveLogPrefix} <- legacy KeepAliveRequest received. requestTs={FormatTimestamp(request?.Timestamp ?? 0)}, at={FormatTimestamp(nowMs)}");

                var response = new KeepAliveResponse
                {
                    Timestamp = nowMs
                };
                await _transport.Send(response);

                if (!IsCurrentRun(cancellationToken))
                    return;

                LogKeepAlive($"{KeepAliveLogPrefix} -> legacy KeepAliveResponse sent. ts={FormatTimestamp(response.Timestamp)}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                LogKeepAliveError($"{KeepAliveLogPrefix} failed to reply legacy KeepAliveResponse: {ex.Message}");
            }
        }

        private async Task RespondToServerKeepAliveAsync(CancellationToken cancellationToken)
        {
            if (!IsCurrentRun(cancellationToken))
                return;

            var success = await SendKeepAliveAsync("reply", cancellationToken, triggerTimeoutOnFailure: false);
            if (!success && IsCurrentRun(cancellationToken))
            {
                LogKeepAliveWarning($"{KeepAliveLogPrefix} reply KeepAlive was not sent. Connection may already be closing.");
            }
        }

        internal int BackgroundTaskCount
        {
            get
            {
                lock (_lock)
                    return _backgroundTasks.Count;
            }
        }

        internal Task WaitForBackgroundTasksAsync()
        {
            lock (_lock)
            {
                return _backgroundTasks.Count == 0
                    ? Task.CompletedTask
                    : Task.WhenAll(new List<Task>(_backgroundTasks));
            }
        }

        private bool TryGetRunningToken(out CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (!_isRunning || _cts == null || _cts.IsCancellationRequested)
                {
                    cancellationToken = default;
                    return false;
                }

                cancellationToken = _cts.Token;
                return true;
            }
        }

        private bool IsCurrentRun(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return _isRunning &&
                       _cts != null &&
                       !cancellationToken.IsCancellationRequested &&
                       _cts.Token == cancellationToken;
            }
        }

        private void TrackBackgroundTask(Task task)
        {
            if (task == null)
                return;

            lock (_lock)
                TrackBackgroundTaskLocked(task);
        }

        private void TrackBackgroundTaskLocked(Task task)
        {
            _backgroundTasks.Add(task);
            _ = task.ContinueWith(
                completedTask =>
                {
                    _ = completedTask.Exception;
                    lock (_lock)
                        _backgroundTasks.Remove(completedTask);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void LogKeepAlive(string message)
        {
#if PlayServ_Logs
            _logger.Log(message);
#endif
        }

        private void LogKeepAliveWarning(string message)
        {
#if PlayServ_Logs
            _logger.LogWarning(message);
#endif
        }

        private void LogKeepAliveError(string message)
        {
#if PlayServ_Logs
            _logger.LogError(message);
#endif
        }

        private static async Task DelayWithCancellationAsync(int delayMs, CancellationToken ct)
        {
            if (delayMs <= 0)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds >= delayMs)
                    break;

                await Task.Yield();
            }

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
#else
            await Task.Delay(delayMs, ct);
#endif
        }

        private static string FormatTimestamp(long timestampMs)
        {
            if (timestampMs <= 0)
                return "n/a";

            try
            {
                var utc = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);
                return $"{timestampMs} ({utc:O})";
            }
            catch (ArgumentOutOfRangeException)
            {
                return timestampMs.ToString();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
            }

            Stop();
        }
    }
}
