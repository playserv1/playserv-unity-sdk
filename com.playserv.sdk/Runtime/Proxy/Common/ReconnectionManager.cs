using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using ILogger = Playserv.Proxy.Logging.ILogger;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace Playserv.Proxy.Common
{
    public sealed class ReconnectionManager : IDisposable
    {
        private readonly ITransport _transport;
        private readonly ILogger _logger;
        private readonly Action<PlayServState> _stateSetter;

        private bool _shouldReconnect;
        private bool _isDisposed;
        private bool _isReconnecting;
        private CancellationTokenSource _reconnectCts;
        private readonly object _reconnectLock = new object();

        public ReconnectionManager(
            ITransport transport,
            ILogger logger,
            Action<PlayServState> stateSetter)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateSetter = stateSetter ?? throw new ArgumentNullException(nameof(stateSetter));
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
            int delayMs = 1000;
            int attempt = 0;

            lock (_reconnectLock)
            {
                _reconnectCts?.Cancel();
                _reconnectCts = new CancellationTokenSource();
            }

            var cts = _reconnectCts;

            while (!cts.IsCancellationRequested)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    lock (_reconnectLock)
                    {
                        _shouldReconnect = false;
                        _isReconnecting = false;
                    }

                    _logger.Log("Editor is not in play mode. Stopping reconnection attempts.");
                    return;
                }
#endif

                lock (_reconnectLock)
                {
                    if (!_shouldReconnect || _isDisposed)
                        return;
                }

                try
                {
                    await Task.Delay(delayMs, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (cts.IsCancellationRequested)
                    return;

                attempt++;
                _logger.Log($"Reconnection attempt {attempt}...");

                try
                {
                    var result = await _transport.Connect();
                    if (result)
                    {
                        lock (_reconnectLock)
                        {
                            _isReconnecting = false;
                        }

                        _stateSetter(PlayServState.Online);
                        _logger.Log("Reconnection successful.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Reconnection attempt {attempt} failed: {ex.Message}");
                }

                delayMs = Math.Min(delayMs * 2, maxDelayMs);
            }

            lock (_reconnectLock)
            {
                _isReconnecting = false;
            }
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
