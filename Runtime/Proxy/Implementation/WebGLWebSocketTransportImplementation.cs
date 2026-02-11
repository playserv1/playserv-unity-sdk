using System;
using System.Collections.Generic;
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
        private readonly Uri _uri;
        private readonly ILogger _logger;
        private readonly ByteArrayChannel _channel;
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private readonly object _gate = new object();
        private readonly SynchronizationContext _syncContext;

        private bool _isConnected;
        private bool _isDisposed;

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
        private static extern void Ws_Send(string message);

        [DllImport("__Internal")]
        private static extern void Ws_Close();

        public Task<bool> Connect()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WebGLWebSocketTransportImplementation));

            if (_isConnected)
            {
                _logger.LogWarning("WebGL WebSocket is already connected.");
                return Task.FromResult(true);
            }

            var tcs = new TaskCompletionSource<bool>();
            var bridge = WebGLWebSocketBridge.Instance;

            void HandleOpen()
            {
                bridge.Opened -= HandleOpen;
                bridge.ErrorReceived -= HandleError;
                bridge.Closed -= HandleClosed;

                _isConnected = true;
                _logger.Log("WebGL WebSocket connected successfully.");
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(true);
            }

            void HandleError(string error)
            {
                bridge.Opened -= HandleOpen;
                bridge.ErrorReceived -= HandleError;
                bridge.Closed -= HandleClosed;

                _logger.LogError($"WebGL WebSocket error: {error}");
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(false);
            }

            void HandleClosed(string reason)
            {
                bridge.Opened -= HandleOpen;
                bridge.ErrorReceived -= HandleError;
                bridge.Closed -= HandleClosed;

                _isConnected = false;
                _logger.LogWarning($"WebGL WebSocket closed: {reason}");
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(false);

                _channel.Complete();
            }

            bridge.Opened += HandleOpen;
            bridge.ErrorReceived += HandleError;
            bridge.Closed += HandleClosed;
            bridge.MessageReceived += HandleMessage;

            try
            {
                _logger.Log($"Connecting WebGL WebSocket to: {_uri}");
                Ws_Connect(bridge.GameObjectName, _uri.ToString());
            }
            catch (Exception ex)
            {
                bridge.Opened -= HandleOpen;
                bridge.ErrorReceived -= HandleError;
                bridge.Closed -= HandleClosed;
                bridge.MessageReceived -= HandleMessage;

                _logger.LogError($"Failed to start WebGL WebSocket connection: {ex.Message}");
                tcs.TrySetResult(false);
            }

            return tcs.Task;
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
                Ws_Send(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send data via WebGL WebSocket: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
        }

        public IObservable<byte[]> OnReceive() => _channel;

        private void HandleMessage(string data)
        {
            if (data == null)
                return;

            var bytes = Encoding.UTF8.GetBytes(data);
            _channel.Next(bytes);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isConnected = false;

            try
            {
                Ws_Close();
            }
            catch
            {
            }

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

