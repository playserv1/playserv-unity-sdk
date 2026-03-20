using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
#if UNITY_WEBGL && !UNITY_EDITOR
    public sealed class WebGLWebSocketTransportImplementation : ITransportImplementation
    {
        private const int ConnectTimeoutMs = 12000;

        private readonly Uri _uri;
        private readonly ILogger _logger;
        private ByteArrayChannel _channel;
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

        public WebGLWebSocketTransportImplementation(string uri, ILogger logger = null)
        {
            _uri = new Uri(uri);
            _logger = logger ?? new ConsoleLogger();
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ByteArrayChannel(_observers, _gate, _syncContext);
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
            pendingConnect?.TrySetResult(false);

            if (wasConnected)
                CompleteAll();
        }

        private void ResetChannel()
        {
            lock (_gate)
            {
                _observers.Clear();
                _channel = new ByteArrayChannel(_observers, _gate, _syncContext);
            }
        }

        private async Task WatchConnectTimeoutAsync(TaskCompletionSource<bool> connectTcs)
        {
            var completed = await WaitForCompletionOrTimeoutAsync(connectTcs.Task, ConnectTimeoutMs);
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

        private static async Task<bool> WaitForCompletionOrTimeoutAsync(Task task, int timeoutMs)
        {
            if (task.IsCompleted)
                return true;

            if (timeoutMs <= 0)
                timeoutMs = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
            var stopwatch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    return false;

                await Task.Yield();
            }

            return true;
#else
            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(task, timeoutTask);
            return completedTask == task;
#endif
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

        private sealed class ByteArrayChannel : IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers;
            private readonly object _gate;
            private readonly SynchronizationContext _syncContext;
            private bool _completed;

            public ByteArrayChannel(List<IObserver<byte[]>> observers, object gate, SynchronizationContext syncContext)
            {
                _observers = observers;
                _gate = gate;
                _syncContext = syncContext ?? new SynchronizationContext();
            }

            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                if (observer == null)
                    throw new ArgumentNullException(nameof(observer));

                lock (_gate)
                {
                    if (_completed)
                    {
                        observer.OnCompleted();
                        return new Unsubscriber(_observers, observer, _gate, false);
                    }

                    _observers.Add(observer);
                    return new Unsubscriber(_observers, observer, _gate, true);
                }
            }

            public void Next(byte[] value)
            {
                IObserver<byte[]>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    snapshot = _observers.ToArray();
                }

                foreach (var o in snapshot)
                {
                    _syncContext.Post(_ => o.OnNext(value), null);
                }
            }

            public void Complete()
            {
                IObserver<byte[]>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    _completed = true;
                    snapshot = _observers.ToArray();
                    _observers.Clear();
                }

                foreach (var o in snapshot)
                {
                    _syncContext.Post(_ => o.OnCompleted(), null);
                }
            }

            public bool IsCompleted
            {
                get
                {
                    lock (_gate)
                    {
                        return _completed;
                    }
                }
            }

            private sealed class Unsubscriber : IDisposable
            {
                private readonly List<IObserver<byte[]>> _observers;
                private readonly IObserver<byte[]> _observer;
                private readonly object _gate;
                private bool _active;

                public Unsubscriber(List<IObserver<byte[]>> observers, IObserver<byte[]> observer, object gate, bool active)
                {
                    _observers = observers;
                    _observer = observer;
                    _gate = gate;
                    _active = active;
                }

                public void Dispose()
                {
                    if (!_active)
                        return;

                    _active = false;

                    lock (_gate)
                    {
                        _observers.Remove(_observer);
                    }
                }
            }
        }
    }
#endif
}
