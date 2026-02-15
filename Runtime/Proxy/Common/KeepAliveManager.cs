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

        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly object _lock = new();

        private CancellationTokenSource _cts;
        private IDisposable _pongSubscription;
        private IDisposable _pingSubscription;
        private IDisposable _eventKeepAliveSubscription;
        private Task _pingLoop;
        private TaskCompletionSource<bool> _pongTcs;
        private bool _isRunning;
        private bool _lastPingSendFailed;
        private int _pingSequence;
        private int _activePingSequence;
        private long _activePingSentAtMs;
        private long _lastPongReceivedAtMs;
        private long _lastServerPingAtMs;

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

                _pongSubscription = _transport.OnReceive<KeepAliveResponse>()
                    .Subscribe(OnPongReceived);

                _pingSubscription = _transport.OnReceive<KeepAliveRequest>()
                    .Subscribe(OnPingReceived);

                _eventKeepAliveSubscription = _transport.OnReceive<EventMessage>()
                    .Subscribe(OnEventMessageReceived);

                _pingLoop = Task.Run(() => PingLoopAsync(_cts.Token));
                _logger.Log(
                    $"{KeepAliveLogPrefix} manager started. pingInterval={PingIntervalMs}ms, pongTimeout={PongTimeoutMs}ms");
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
                _pongSubscription?.Dispose();
                _pingSubscription?.Dispose();
                _eventKeepAliveSubscription?.Dispose();

                _pongSubscription = null;
                _pingSubscription = null;
                _eventKeepAliveSubscription = null;

                _logger.Log(
                    $"{KeepAliveLogPrefix} manager stopped. lastPingAt={FormatTimestamp(Interlocked.Read(ref _activePingSentAtMs))}, " +
                    $"lastPongAt={FormatTimestamp(Interlocked.Read(ref _lastPongReceivedAtMs))}, " +
                    $"lastServerPingAt={FormatTimestamp(Interlocked.Read(ref _lastServerPingAtMs))}");
            }
        }

        private async Task PingLoopAsync(CancellationToken cancellationToken)
        {
            var firstPingDelayMs = Math.Min(PingIntervalMs, 5000);
            _logger.Log(
                $"{KeepAliveLogPrefix} ping loop started. first ping in {firstPingDelayMs}ms");

            var isFirstPing = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var delayMs = isFirstPing ? firstPingDelayMs : PingIntervalMs;
                    isFirstPing = false;
                    await Task.Delay(delayMs, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var success = await SendPingAndWaitForPongAsync(cancellationToken);
                    if (!success)
                    {
                        if (_lastPingSendFailed)
                        {
                            _logger.LogWarning(
                                $"{KeepAliveLogPrefix} ping send failed for ping#{Volatile.Read(ref _activePingSequence)}. " +
                                "Triggering timeout handler.");
                        }
                        else
                        {
                            _logger.LogWarning(
                                $"{KeepAliveLogPrefix} pong not received in time for ping#{Volatile.Read(ref _activePingSequence)}. " +
                                $"waited={PongTimeoutMs}ms, lastPingAt={FormatTimestamp(Interlocked.Read(ref _activePingSentAtMs))}, " +
                                $"lastPongAt={FormatTimestamp(Interlocked.Read(ref _lastPongReceivedAtMs))}");
                        }
                        OnTimeout?.Invoke();
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.Log($"{KeepAliveLogPrefix} ping loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{KeepAliveLogPrefix} error: {ex.Message}");
                    OnTimeout?.Invoke();
                    break;
                }
            }
        }

        private async Task<bool> SendPingAndWaitForPongAsync(CancellationToken cancellationToken)
        {
            var pongTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pongTcs = pongTcs;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sequence = Interlocked.Increment(ref _pingSequence);
            Interlocked.Exchange(ref _activePingSequence, sequence);
            Interlocked.Exchange(ref _activePingSentAtMs, nowMs);

            var ping = new KeepAliveRequest
            {
                Timestamp = nowMs
            };

            _logger.Log(
                $"{KeepAliveLogPrefix} -> ping#{sequence} send attempt. ts={FormatTimestamp(ping.Timestamp)}");

            _lastPingSendFailed = false;
            try
            {
                await _transport.Send(ping);
            }
            catch (Exception ex)
            {
                _lastPingSendFailed = true;
                _logger.LogWarning($"{KeepAliveLogPrefix} ping#{sequence} send failed: {ex.Message}");
                return false;
            }

            OnPingSent?.Invoke();
            _logger.Log(
                $"{KeepAliveLogPrefix} -> ping#{sequence} sent. ts={FormatTimestamp(ping.Timestamp)}, timeout={PongTimeoutMs}ms");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PongTimeoutMs);

            try
            {
                var timeoutTask = Task.Delay(PongTimeoutMs, timeoutCts.Token);
                var completedTask = await Task.WhenAny(pongTcs.Task, timeoutTask);

                return completedTask == pongTcs.Task && await pongTcs.Task;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                if (ReferenceEquals(_pongTcs, pongTcs))
                    _pongTcs = null;
            }
        }

        private void OnPongReceived(KeepAliveResponse response)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Interlocked.Exchange(ref _lastPongReceivedAtMs, nowMs);
            var sequence = Volatile.Read(ref _activePingSequence);
            var pingSentAtMs = Interlocked.Read(ref _activePingSentAtMs);
            var rttMs = pingSentAtMs > 0 ? Math.Max(0, nowMs - pingSentAtMs) : -1;

            PongReceived?.Invoke();
            _logger.Log(
                $"{KeepAliveLogPrefix} <- pong for ping#{sequence}. responseTs={FormatTimestamp(response?.Timestamp ?? 0)}, rtt~{rttMs}ms");
            _pongTcs?.TrySetResult(true);
        }

        private void OnEventMessageReceived(EventMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.EventType))
                return;

            if (!IsKeepAliveEventType(message.EventType))
                return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Interlocked.Exchange(ref _lastPongReceivedAtMs, nowMs);
            var sequence = Volatile.Read(ref _activePingSequence);
            var pingSentAtMs = Interlocked.Read(ref _activePingSentAtMs);
            var rttMs = pingSentAtMs > 0 ? Math.Max(0, nowMs - pingSentAtMs) : -1;
            if (ShouldAcknowledgeInfrastructureEvent(message.EventType))
            {
                Interlocked.Exchange(ref _lastServerPingAtMs, nowMs);
            }

            PongReceived?.Invoke();
            _logger.Log(
                $"{KeepAliveLogPrefix} <- infrastructure event '{message.EventType}' treated as pong for ping#{sequence}. rtt~{rttMs}ms");
            if (ShouldAcknowledgeInfrastructureEvent(message.EventType))
            {
                _ = SendInfrastructurePongAckAsync(message.EventType);
            }
            _pongTcs?.TrySetResult(true);
        }

        private static bool IsKeepAliveEventType(string eventTypeName)
        {
            return string.Equals(eventTypeName, "KeepAlive", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Heartbeat", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Ping", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Pong", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldAcknowledgeInfrastructureEvent(string eventTypeName)
        {
            return string.Equals(eventTypeName, "KeepAlive", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Heartbeat", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(eventTypeName, "Ping", StringComparison.OrdinalIgnoreCase);
        }

        private async Task SendInfrastructurePongAckAsync(string eventTypeName)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var response = new KeepAliveResponse
            {
                Timestamp = nowMs
            };

            try
            {
                await _transport.Send(response);
                _logger.Log(
                    $"{KeepAliveLogPrefix} -> pong ack sent for infrastructure event '{eventTypeName}'. ts={FormatTimestamp(nowMs)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    $"{KeepAliveLogPrefix} failed to send pong ack for infrastructure event '{eventTypeName}': {ex.Message}");
            }
        }

        private async void OnPingReceived(KeepAliveRequest request)
        {
            try
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Interlocked.Exchange(ref _lastServerPingAtMs, nowMs);
                _logger.Log(
                    $"{KeepAliveLogPrefix} <- server ping received. requestTs={FormatTimestamp(request?.Timestamp ?? 0)}, at={FormatTimestamp(nowMs)}");

                var response = new KeepAliveResponse
                {
                    Timestamp = nowMs
                };
                await _transport.Send(response);
                _logger.Log($"{KeepAliveLogPrefix} -> pong sent to server. ts={FormatTimestamp(response.Timestamp)}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{KeepAliveLogPrefix} failed to reply pong: {ex.Message}");
            }
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
