using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Wrapper;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.Proxy.Common
{
    public sealed class ReconnectionManager : IDisposable
    {
        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly Action<PlayServState> _stateSetter;
        private readonly Func<Task<bool>> _canReconnect;
        private readonly Func<Task<bool>> _connectAction;

        private bool _shouldReconnect;
        private volatile bool _isDisposed;
        private bool _isReconnecting;
        private CancellationTokenSource _reconnectCts;
        private readonly object _reconnectLock = new object();

        public ReconnectionManager(
            ITransport transport,
            ILogger logger,
            Action<PlayServState> stateSetter,
            Func<Task<bool>> canReconnect = null,
            Func<Task<bool>> connectAction = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateSetter = stateSetter ?? throw new ArgumentNullException(nameof(stateSetter));
            _canReconnect = canReconnect ?? (() => Task.FromResult(true));
            _connectAction = connectAction;
        }

        public void Start()
        {
            lock (_reconnectLock)
            {
                _shouldReconnect = true;
            }
        }

        public void Stop()
        {
            lock (_reconnectLock)
            {
                _shouldReconnect = false;
                _reconnectCts?.Cancel();
            }
        }

        public void HandleConnectionLost()
        {
            lock (_reconnectLock)
            {
                if (!_shouldReconnect || _isDisposed || _isReconnecting)
                    return;

                _isReconnecting = true;
            }

            _stateSetter(PlayServState.Reconnecting);
            _logger.LogWarning("Connection lost. Starting reconnection...");

            _ = ReconnectAsync();
        }

        private async Task ReconnectAsync()
        {
            const int maxDelayMs = 30000;
            const int connectionAttemptTimeoutMs = 30000;
            int delayMs = 1000;
            int attempt = 0;

            bool reconnected = false;

            lock (_reconnectLock)
            {
                _reconnectCts?.Cancel();
                _reconnectCts = new CancellationTokenSource();
            }

            var cts = _reconnectCts;
            
            try
            {
                await DelayWithCancellationAsync(2000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                lock (_reconnectLock)
                {
                    if (cts == _reconnectCts)
                        _isReconnecting = false;
                }

                _logger.LogWarning("Reconnection loop canceled before first attempt.");
                return;
            }

            while (!cts.IsCancellationRequested)
            {
                bool canContinue;
                try
                {
                    canContinue = await _canReconnect();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"The _canReconnect check failed with an exception, stopping reconnection: {ex}");
                    canContinue = false;
                }

                if (!canContinue)
                {
                    lock (_reconnectLock)
                    {
                        _shouldReconnect = false;
                        _isReconnecting = false;
                    }

                    _logger.Log("Reconnection environment is not ready. Stopping reconnection attempts.");

                    if (!_isDisposed)
                    {
                        _stateSetter(PlayServState.Offline);
                    }
                    return;
                }

                lock (_reconnectLock)
                {
                    if (!_shouldReconnect || _isDisposed)
                        break;
                }

                if (attempt > 0)
                {
                    try
                    {
                        await DelayWithCancellationAsync(delayMs, cts.Token);
                        delayMs = Math.Min(delayMs * 2, maxDelayMs);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (cts.IsCancellationRequested)
                    break;

                attempt++;
                _logger.Log($"Reconnection attempt {attempt}...");

                try
                {
                    var connectTask = _connectAction != null ? _connectAction() : _transport.Connect();
                    var completedInTime = await WaitForTaskOrTimeoutAsync(
                        connectTask,
                        connectionAttemptTimeoutMs,
                        cts.Token);

                    if (!completedInTime)
                    {
                        _logger.LogError(
                            $"Reconnection attempt {attempt} timed out after {connectionAttemptTimeoutMs / 1000}s.");
                    }
                    else
                    {
                        var result = await connectTask; // Await to get result or propagate exception
                        if (result)
                        {
                            reconnected = true;
                            break;
                        }

                        _logger.LogWarning($"Reconnection attempt {attempt} failed (transport returned false).");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Reconnection attempt {attempt} failed: {ex.Message}");
                }
            }

            lock (_reconnectLock)
            {
                if (cts != _reconnectCts)
                {
                    return;
                }
                _isReconnecting = false;
            }

            if (reconnected)
            {
                _stateSetter(PlayServState.Online);
                _logger.Log("Reconnection successful.");
            }
            else if (!_isDisposed)
            {
                _stateSetter(PlayServState.Offline);
                _logger.LogWarning("Reconnection stopped or failed. State set to Offline.");
            }
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

        private static async Task<bool> WaitForTaskOrTimeoutAsync(Task task, int timeoutMs, CancellationToken ct)
        {
            if (task.IsCompleted)
                return true;

            if (timeoutMs <= 0)
                timeoutMs = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                if (task.IsCompleted)
                    return true;

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    return false;

                await Task.Yield();
            }

            throw new OperationCanceledException(ct);
#else
            var timeoutTask = Task.Delay(timeoutMs, ct);
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == task)
                return true;

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            return false;
#endif
        }

        public void Dispose()
        {
            _isDisposed = true;

            lock (_reconnectLock)
            {
                _shouldReconnect = false;
                _reconnectCts?.Cancel();
                _reconnectCts?.Dispose();
            }
        }
    }
}
