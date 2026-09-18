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
#if UNITY_WEBGL || UNITY_EDITOR
    internal interface IWebGLWebSocketApi
    {
        void Connect(string connectionId, string url);
        int Send(string connectionId, string message);
        void Close(string connectionId);
    }

    /// <summary>
    /// A text WebSocket transport with an independent browser socket and callback
    /// target for each connection attempt. Reset and disposal affect only this instance.
    /// </summary>
#if UNITY_EDITOR
    internal sealed class WebGLWebSocketTransportImplementation : ITransportImplementation, IPlayServTransportCloseInfoSource
#else
    public sealed class WebGLWebSocketTransportImplementation : ITransportImplementation, IPlayServTransportCloseInfoSource
#endif
    {
        private const int ConnectTimeoutMs = 12000;

        private readonly Uri _uri;
        private readonly ILogger _logger;
        private readonly IWebGLWebSocketApi _api;
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

        /// <summary>Creates a transport without opening its browser socket.</summary>
        public WebGLWebSocketTransportImplementation(string uri, ILogger logger = null)
            : this(uri, logger, new NativeApi())
        {
        }

        internal WebGLWebSocketTransportImplementation(string uri, ILogger logger, IWebGLWebSocketApi api)
        {
            _uri = new Uri(uri);
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ObservableByteChannel(_observers, _gate, _syncContext);
        }

        [DllImport("__Internal")]
        private static extern void Ws_Connect(string gameObjectName, string url);

        [DllImport("__Internal")]
        private static extern int Ws_Send(string connectionId, [In] byte[] message, int messageByteLength);

        [DllImport("__Internal")]
        private static extern void Ws_Close(string connectionId);

        private sealed class NativeApi : IWebGLWebSocketApi
        {
            public void Connect(string connectionId, string url) => Ws_Connect(connectionId, url);
            public int Send(string connectionId, string message)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                return Ws_Send(connectionId, bytes, bytes.Length);
            }
            public void Close(string connectionId) => Ws_Close(connectionId);
        }

        public Task<bool> Connect()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WebGLWebSocketTransportImplementation));

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
                EnsureBridgeEventHandlers();
                _logger.Log("Connecting WebGL WebSocket.");
                _api.Connect(_bridge.GameObjectName, _uri.ToString());
            }
            catch (Exception)
            {
                _logger.LogError("Failed to start WebGL WebSocket connection.");
                lock (_connectGate)
                {
                    if (ReferenceEquals(_connectTcs, connectTcs))
                    {
                        _connecting = false;
                        _connectTcs = null;
                    }
                }
                TryCloseSocket();
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
                var sendResult = _api.Send(_bridge.GameObjectName, message);
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
            catch (Exception)
            {
                _logger.LogError("Failed to send data via WebGL WebSocket.");
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

            TryCloseSocket();
            ResetChannel();
            pendingConnect?.TrySetResult(false);
            _logger.Log("WebGL WebSocket transport connection state reset.");
        }

        private void CompleteAll() => _channel.Complete();

        private void EnsureBridgeEventHandlers()
        {
            if (_bridgeEventsSubscribed)
                return;

            _bridge = WebGLWebSocketBridge.Create();
            _bridge.Opened += OnBridgeOpened;
            _bridge.MessageReceived += OnBridgeMessageReceived;
            _bridge.ErrorReceived += OnBridgeErrorReceived;
            _bridge.Closed += OnBridgeClosed;
            _bridgeEventsSubscribed = true;
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
            if (data == null || _isDisposed || !_isConnected)
                return;

            var byteCount = Encoding.UTF8.GetByteCount(data);
            if (byteCount > PlayServWebSocketPayloadLimits.MaxMessageBytes)
            {
                var exception = new PlayServWebSocketPayloadException(
                    PlayServWebSocketPayloadDirection.Inbound,
                    byteCount);
                _logger.LogError(exception.Message);
                _isConnected = false;
                _channel.Error(exception);
                TryCloseSocket();
                Closed?.Invoke(new PlayServTransportCloseInfo(1009, "message too large"));
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

            _logger.LogError("WebGL WebSocket connection failed.");
            TryCloseSocket();
            if (wasConnected)
                CompleteAll();
            pendingConnect?.TrySetResult(false);
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

            _logger.LogWarning("WebGL WebSocket closed.");
            TryCloseSocket();
            if (wasConnected)
                CompleteAll();
            try
            {
                Closed?.Invoke(ParseCloseInfo(reason));
            }
            finally
            {
                pendingConnect?.TrySetResult(false);
            }
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
            var bridge = _bridge;
            _bridge = null;
            _bridgeEventsSubscribed = false;
            if (bridge == null)
                return;

            var connectionId = bridge.GameObjectName;
            bridge.Retire();
            try
            {
                _api.Close(connectionId);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            TaskCompletionSource<bool> pendingConnect;
            lock (_connectGate)
            {
                _isDisposed = true;
                _isConnected = false;
                _connecting = false;
                pendingConnect = _connectTcs;
                _connectTcs = null;
            }

            TryCloseSocket();

            _channel.Complete();
            pendingConnect?.TrySetResult(false);
        }
    }
#endif
}
