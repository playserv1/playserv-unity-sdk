using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;

namespace Playserv.Proxy.Implementation
{
#if UNITY_WEBGL && !UNITY_EDITOR
    public sealed class WebGLWebSocketTransportImplementation : ITransportImplementation, IPlayServTransportCloseInfoSource
    {
        private const int ConnectTimeoutMs = 12000;

        private readonly Uri _uri;
        private readonly ILogger _logger;
        private ObservableByteChannel _channel;
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private readonly object _gate = new object();
        private readonly object _connectGate = new object();
        private readonly SynchronizationContext _syncContext;
        private WebGLWebSocketBridge _bridge;

        private bool _isConnected;
        private bool _isDisposed;
        private bool _connecting;
        private bool _bridgeEventsSubscribed;
        private TaskCompletionSource<bool> _connectTcs;

        public event Action<PlayServTransportCloseInfo> Closed;

        public WebGLWebSocketTransportImplementation(string uri, ILogger logger = null)
        {
            _uri = new Uri(uri);
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ObservableByteChannel(_observers, _gate, _syncContext);
        }

        [DllImport("__Internal")]
        private static extern void Ws_Connect(string gameObjectName, string url);

        [DllImport("__Internal")]
        private static extern int Ws_Send(string message);

        [DllImport("__Internal")]
        private static extern void Ws_Close();

        public Task<bool> Connect()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WebGLWebSocketTransportImplementation));

            EnsureBridgeEventHandlers();

            TaskCompletionSource<bool> connectTcs;
            lock (_connectGate)
            {
                if (_connecting)
                {
                    _logger.LogWarning("WebGL WebSocket connect already in progress.");
                    return _connectTcs?.Task ?? Task.FromResult(false);
                }

                if (_isConnected)
                {
                    if (_channel.IsCompleted)
                    {
                        _logger.LogWarning("WebGL socket is marked connected but channel is completed. Recreating receive channel.");
                        _isConnected = false;
                        ResetChannel();
                    }
                    else
                    {
                        _logger.LogWarning("WebGL WebSocket is already connected.");
                        return Task.FromResult(true);
                    }
                }
                else if (_channel.IsCompleted)
                {
                    ResetChannel();
                }

                _connecting = true;
                _isConnected = false;
                _connectTcs = new TaskCompletionSource<bool>();
                connectTcs = _connectTcs;
            }

            try
            {
                _logger.Log($"Connecting WebGL WebSocket to: {_uri}");
                Ws_Connect(_bridge.GameObjectName, _uri.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start WebGL WebSocket connection: {ex.Message}");
                lock (_connectGate)
                {
                    if (ReferenceEquals(_connectTcs, connectTcs))
                    {
                        _connecting = false;
                        _connectTcs = null;
                    }
                }
                connectTcs.TrySetResult(false);
            }

            _ = WatchConnectTimeoutAsync(connectTcs);
            return connectTcs.Task;
        }

        public Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            PlayServWebSocketPayloadLimits.EnsureAllowed(
                data.LongLength,
                PlayServWebSocketPayloadDirection.Outbound);

            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WebGLWebSocketTransportImplementation));

            if (!_isConnected)
            {
                _logger.LogError("Attempted to send data but WebGL WebSocket is not connected.");
                throw new InvalidOperationException("WebGL WebSocket is not connected.");
            }

            try
            {
                var message = Encoding.UTF8.GetString(data);
                var sendResult = Ws_Send(message);
                if (sendResult != 1)
                {
                    _logger.LogError(
                        "WebGL WebSocket send failed: JS layer reported socket is not open or send threw.");
                    _isConnected = false;
                    CompleteAll();
                    TryCloseSocket();
                    throw new InvalidOperationException(
                        "WebGL WebSocket send failed (socket not open or JS send error).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send data via WebGL WebSocket: {ex.Message}");
                _isConnected = false;
                CompleteAll();
                TryCloseSocket();
                throw;
            }

            return Task.CompletedTask;
        }

        public IObservable<byte[]> OnReceive() => _channel;

        public void ResetConnection()
        {
            if (_isDisposed)
                return;

            TaskCompletionSource<bool> pendingConnect = null;
            lock (_connectGate)
            {
                pendingConnect = _connectTcs;
                _connectTcs = null;
                _connecting = false;
                _isConnected = false;
            }

            pendingConnect?.TrySetResult(false);
            TryCloseSocket();
            ResetChannel();
            _logger.Log("WebGL WebSocket transport connection state reset.");
        }

        private void CompleteAll() => _channel.Complete();

        private void EnsureBridgeEventHandlers()
        {
            if (_bridgeEventsSubscribed)
                return;

            _bridge = WebGLWebSocketBridge.Instance;
            _bridge.Opened += OnBridgeOpened;
            _bridge.MessageReceived += OnBridgeMessageReceived;
            _bridge.ErrorReceived += OnBridgeErrorReceived;
            _bridge.Closed += OnBridgeClosed;
            _bridgeEventsSubscribed = true;
        }

        private void RemoveBridgeEventHandlers()
        {
            if (!_bridgeEventsSubscribed || _bridge == null)
                return;

            _bridge.Opened -= OnBridgeOpened;
            _bridge.MessageReceived -= OnBridgeMessageReceived;
            _bridge.ErrorReceived -= OnBridgeErrorReceived;
            _bridge.Closed -= OnBridgeClosed;
            _bridgeEventsSubscribed = false;
        }

        private void OnBridgeOpened()
        {
            TaskCompletionSource<bool> pendingConnect;
            lock (_connectGate)
            {
                if (!_connecting)
                {
                    _logger.LogWarning("WebGL OnWsOpen received without active connect attempt. Ignored.");
                    return;
                }

                _isConnected = true;
                _connecting = false;
                pendingConnect = _connectTcs;
                _connectTcs = null;
            }

            _logger.Log("WebGL WebSocket connected successfully.");
            pendingConnect?.TrySetResult(true);
        }

        private void OnBridgeMessageReceived(string data)
        {
            if (data == null)
                return;

            var byteCount = Encoding.UTF8.GetByteCount(data);
            if (byteCount > PlayServWebSocketPayloadLimits.MaxMessageBytes)
            {
                var exception = new PlayServWebSocketPayloadException(
                    PlayServWebSocketPayloadDirection.Inbound,
                    byteCount);
                _logger.LogError(exception.Message);
                _isConnected = false;
                Closed?.Invoke(new PlayServTransportCloseInfo(1009, "message too large"));
                _channel.Error(exception);
                TryCloseSocket();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(data);
            _channel.Next(bytes);
        }

        private void OnBridgeErrorReceived(string error)
        {
            TaskCompletionSource<bool> pendingConnect;
            var wasConnected = false;
            lock (_connectGate)
            {
                if (!_connecting && !_isConnected)
                    return;

                wasConnected = _isConnected;
                _isConnected = false;
                _connecting = false;
                pendingConnect = _connectTcs;
                _connectTcs = null;
            }

            _logger.LogError($"WebGL WebSocket error: {error}");
            pendingConnect?.TrySetResult(false);

            if (wasConnected)
                CompleteAll();
        }

        private void OnBridgeClosed(string reason)
        {
            TaskCompletionSource<bool> pendingConnect;
            var wasConnected = false;
            lock (_connectGate)
            {
                if (!_connecting && !_isConnected)
                    return;

                wasConnected = _isConnected;
                _isConnected = false;
                _connecting = false;
                pendingConnect = _connectTcs;
                _connectTcs = null;
            }

            _logger.LogWarning($"WebGL WebSocket closed: {reason}");
            Closed?.Invoke(ParseCloseInfo(reason));
            pendingConnect?.TrySetResult(false);

            if (wasConnected)
                CompleteAll();
        }

        private static PlayServTransportCloseInfo ParseCloseInfo(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new PlayServTransportCloseInfo(null, string.Empty);

            var separator = value.IndexOf(':');
            if (separator > 0 && int.TryParse(value.Substring(0, separator), out var statusCode))
            {
                return new PlayServTransportCloseInfo(
                    statusCode,
                    value.Substring(separator + 1).Trim());
            }

            return new PlayServTransportCloseInfo(null, value.Trim());
        }

        private void ResetChannel()
        {
            lock (_gate)
            {
                _observers.Clear();
                _channel = new ObservableByteChannel(_observers, _gate, _syncContext);
            }
        }

        private async Task WatchConnectTimeoutAsync(TaskCompletionSource<bool> connectTcs)
        {
            var completed = await AsyncTimeoutHelper.WaitForCompletionOrTimeoutAsync(connectTcs.Task, ConnectTimeoutMs);
            if (completed)
                return;

            lock (_connectGate)
            {
                if (!ReferenceEquals(_connectTcs, connectTcs) || !_connecting)
                    return;

                _connecting = false;
                _isConnected = false;
                _connectTcs = null;
            }

            _logger.LogWarning($"WebGL WebSocket connect timed out after {ConnectTimeoutMs}ms.");
            TryCloseSocket();
            connectTcs.TrySetResult(false);
        }
        private void TryCloseSocket()
        {
            try
            {
                Ws_Close();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isConnected = false;
            _connecting = false;

            TryCloseSocket();

            RemoveBridgeEventHandlers();
            _channel.Complete();
        }
    }
#endif
}
