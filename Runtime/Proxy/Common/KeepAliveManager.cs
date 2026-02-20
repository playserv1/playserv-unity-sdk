using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Events.Requests;
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

        private CancellationTokenSource _cts;
        private IDisposable _legacyPingSubscription;
        private IDisposable _eventKeepAliveSubscription;
        private Task _sendLoop;
        private Task _monitorLoop;
        private bool _isRunning;
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
                if (_isRunning)
                    return;

                _isRunning = true;
                _cts = new CancellationTokenSource();
                _timeoutRaised = 0;
                _heartbeatSequence = 0;
                Interlocked.Exchange(ref _lastClientKeepAliveAtMs, 0);
                Interlocked.Exchange(ref _lastServerKeepAliveAtMs, 0);
                Interlocked.Exchange(ref _startedAtMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                _legacyPingSubscription = _transport.OnReceive<KeepAliveRequest>()
                    .Subscribe(OnLegacyPingReceived);

                _eventKeepAliveSubscription = _transport.OnReceive<EventMessage>()
                    .Subscribe(OnEventMessageReceived);

                _sendLoop = Task.Run(() => SendLoopAsync(_cts.Token));
                _monitorLoop = Task.Run(() => MonitorLoopAsync(_cts.Token));
                LogKeepAlive(
                    $"{KeepAliveLogPrefix} manager started. twait={ResolveKeepAliveIntervalMs()}ms, " +
                    $"waitWindow={ResolveKeepAliveWaitWindowMs()}ms");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
                _cts?.Cancel();
                _legacyPingSubscription?.Dispose();
                _eventKeepAliveSubscription?.Dispose();

                _legacyPingSubscription = null;
                _eventKeepAliveSubscription = null;

                LogKeepAlive(
                    $"{KeepAliveLogPrefix} manager stopped. lastClientKeepAliveAt={FormatTimestamp(Interlocked.Read(ref _lastClientKeepAliveAtMs))}, " +
                    $"lastServerKeepAliveAt={FormatTimestamp(Interlocked.Read(ref _lastServerKeepAliveAtMs))}");
            }
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            var firstPingDelayMs = Math.Min(ResolveKeepAliveIntervalMs(), 5000);
            LogKeepAlive(
                $"{KeepAliveLogPrefix} heartbeat loop started. first KeepAlive in {firstPingDelayMs}ms");

            var isFirstPing = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var delayMs = isFirstPing ? firstPingDelayMs : ResolveKeepAliveIntervalMs();
                    isFirstPing = false;
                    await Task.Delay(delayMs, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var success = await SendKeepAliveAsync(cancellationToken);
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
                    LogKeepAliveError($"{KeepAliveLogPrefix} send loop error: {ex.Message}");
                    TriggerTimeout("send loop failed");
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
                    await Task.Delay(monitorTickMs, cancellationToken);

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
                    TriggerTimeout("server keepalive timeout");
                    break;
                }
                catch (OperationCanceledException)
                {
                    LogKeepAlive($"{KeepAliveLogPrefix} watchdog loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    LogKeepAliveError($"{KeepAliveLogPrefix} watchdog loop error: {ex.Message}");
                    TriggerTimeout("watchdog loop failed");
                    break;
                }
            }
        }

        private async Task<bool> SendKeepAliveAsync(CancellationToken cancellationToken)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sequence = Interlocked.Increment(ref _heartbeatSequence);
            Interlocked.Exchange(ref _lastClientKeepAliveAtMs, nowMs);

            var keepAliveEvent = new EventMessage(KeepAliveEventType, EmptyPayloadJson);

            LogKeepAlive(
                $"{KeepAliveLogPrefix} -> heartbeat#{sequence} send attempt. event={KeepAliveEventType}, ts={FormatTimestamp(nowMs)}");

            try
            {
                await _transport.Send(keepAliveEvent);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogKeepAliveWarning($"{KeepAliveLogPrefix} heartbeat#{sequence} send failed: {ex.Message}");
                TriggerTimeout("failed to send keepalive");
                return false;
            }

            OnPingSent?.Invoke();
            LogKeepAlive(
                $"{KeepAliveLogPrefix} -> heartbeat#{sequence} sent. twait={ResolveKeepAliveIntervalMs()}ms");
            return true;
        }

        private void OnEventMessageReceived(EventMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.EventType))
                return;

            if (!IsKeepAliveEventType(message.EventType))
                return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var previousServerKeepAliveAtMs = Interlocked.Exchange(ref _lastServerKeepAliveAtMs, nowMs);
            var elapsedMs = previousServerKeepAliveAtMs > 0
                ? Math.Max(0, nowMs - previousServerKeepAliveAtMs)
                : -1;

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
        }

        private static bool IsKeepAliveEventType(string eventTypeName)
        {
            return string.Equals(eventTypeName, KeepAliveEventType, StringComparison.OrdinalIgnoreCase);
        }

        private int ResolveKeepAliveIntervalMs()
        {
            return PingIntervalMs > 0 ? PingIntervalMs : DefaultKeepAliveIntervalMs;
        }

        private int ResolveKeepAliveWaitWindowMs()
        {
            var intervalMs = ResolveKeepAliveIntervalMs();
            return Math.Max(intervalMs, PongTimeoutMs > 0 ? PongTimeoutMs : intervalMs);
        }

        private int ResolveMonitorTickMs()
        {
            var waitWindowMs = ResolveKeepAliveWaitWindowMs();
            var tickMs = waitWindowMs / 4;
            tickMs = Math.Min(tickMs, 2000);
            return Math.Max(tickMs, 500);
        }

        private void TriggerTimeout(string reason)
        {
            if (Interlocked.Exchange(ref _timeoutRaised, 1) != 0)
                return;

            LogKeepAliveWarning($"{KeepAliveLogPrefix} timeout triggered: {reason}");
            OnTimeout?.Invoke();
        }

        private async void OnLegacyPingReceived(KeepAliveRequest request)
        {
            try
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LogKeepAlive(
                    $"{KeepAliveLogPrefix} <- legacy KeepAliveRequest received. requestTs={FormatTimestamp(request?.Timestamp ?? 0)}, at={FormatTimestamp(nowMs)}");

                var response = new KeepAliveResponse
                {
                    Timestamp = nowMs
                };
                await _transport.Send(response);
                LogKeepAlive($"{KeepAliveLogPrefix} -> legacy KeepAliveResponse sent. ts={FormatTimestamp(response.Timestamp)}");
            }
            catch (Exception ex)
            {
                LogKeepAliveError($"{KeepAliveLogPrefix} failed to reply legacy KeepAliveResponse: {ex.Message}");
            }
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
            Stop();
            _cts?.Dispose();
        }
    }
}
