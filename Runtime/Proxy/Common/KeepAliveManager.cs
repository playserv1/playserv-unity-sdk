using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Common
{
    public sealed class KeepAliveManager : IDisposable
    {
        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly object _lock = new();

        private CancellationTokenSource _cts;
        private IDisposable _pongSubscription;
        private IDisposable _pingSubscription;
        private Task _pingLoop;
        private TaskCompletionSource<bool> _pongTcs;
        private bool _isRunning;

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

                _pingLoop = Task.Run(() => PingLoopAsync(_cts.Token));
                _logger.Log("KeepAlive manager started.");
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

                _pongSubscription = null;
                _pingSubscription = null;

                _logger.Log("KeepAlive manager stopped.");
            }
        }

        private async Task PingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PingIntervalMs, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var success = await SendPingAndWaitForPongAsync(cancellationToken);
                    if (!success)
                    {
                        _logger.LogWarning("KeepAlive pong not received in time.");
                        OnTimeout?.Invoke();
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"KeepAlive error: {ex.Message}");
                }
            }
        }

        private async Task<bool> SendPingAndWaitForPongAsync(CancellationToken cancellationToken)
        {
            _pongTcs = new TaskCompletionSource<bool>();

            var ping = new KeepAliveRequest
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await _transport.Send(ping);
            OnPingSent?.Invoke();
            _logger.Log($"KeepAlive ping sent: {ping.Timestamp}");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PongTimeoutMs);

            try
            {
                var timeoutTask = Task.Delay(PongTimeoutMs, timeoutCts.Token);
                var completedTask = await Task.WhenAny(_pongTcs.Task, timeoutTask);

                return completedTask == _pongTcs.Task && await _pongTcs.Task;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private void OnPongReceived(KeepAliveResponse response)
        {
            PongReceived?.Invoke();
            _logger.Log($"KeepAlive pong received: {response.Timestamp}");
            _pongTcs?.TrySetResult(true);
        }

        private async void OnPingReceived(KeepAliveRequest request)
        {
            _logger.Log($"KeepAlive ping received from server: {request.Timestamp}");

            var response = new KeepAliveResponse();
            await _transport.Send(response);
            _logger.Log("KeepAlive pong sent to server.");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
