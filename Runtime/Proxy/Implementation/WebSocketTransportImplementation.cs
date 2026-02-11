using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
    public sealed class WebSocketTransportImplementation : ITransportImplementation
    {
        private readonly Uri _uri;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private readonly ByteArrayChannel _channel;
        private readonly ILogger _logger;
        private readonly object _connectGate = new object();
        
        private bool _connecting;
        private ClientWebSocket _socket = new ClientWebSocket();
        private CancellationTokenSource _connectCTS;
        private Task _receiveLoop;

        public WebSocketTransportImplementation(string uri, ILogger logger = null)
        {
            _uri = new Uri(uri);
            _logger = logger ?? new ConsoleLogger();
            var syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ByteArrayChannel(_observers, _gate, syncContext);
        }

        public async Task<bool> Connect()
        {
            lock (_connectGate)
            {
                if (_connecting)
                {
                    _logger.LogWarning("WebSocket connect already in progress.");
                    return false;
                }

                if (_socket != null && _socket.State == WebSocketState.Open)
                {
                    _logger.LogWarning("WebSocket is already connected.");
                    return true;
                }
                
                _socket?.Dispose();
                _socket = new ClientWebSocket();

                _connectCTS?.Dispose();
                _connectCTS = new CancellationTokenSource();

                _connecting = true;
            }

            try
            {
                _logger.Log($"Connecting to WebSocket: {_uri}");
                await _socket.ConnectAsync(_uri, _connectCTS.Token);

                _receiveLoop = Task.Run(ReceiveLoop);

                _logger.Log("WebSocket connected successfully.");
                return true;
            }
            catch (OperationCanceledException)
            {
                try { _socket.Abort(); }
                catch
                {
                    // ignored
                }

                _logger.LogWarning("WebSocket connect cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to connect to WebSocket: {ex.Message}");
                return false;
            }
            finally
            {
                lock (_connectGate) { _connecting = false; }
            }
        }

        public async Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (_socket.State != WebSocketState.Open)
            {
                _logger.LogError("Attempted to send data but WebSocket is not connected.");
                throw new InvalidOperationException("WebSocket is not connected.");
            }

            try
            {
                var segment = new ArraySegment<byte>(data);
                await _socket.SendAsync(
                    segment,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send data via WebSocket: {ex.Message}");
                throw;
            }
        }

        public IObservable<byte[]> OnReceive() => _channel;

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            using var ms = new MemoryStream();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _socket.ReceiveAsync(segment, _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        CompleteAll();
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (!result.EndOfMessage)
                        continue;

                    var data = ms.ToArray();
                    ms.SetLength(0);

                    NextAll(data);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Log("WebSocket receive loop cancelled.");
                CompleteAll();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in WebSocket receive loop: {ex.Message}");
                ErrorAll(ex);
            }
        }

        private void CompleteAll() => _channel.Complete();

        private void ErrorAll(Exception ex) => _channel.Error(ex);

        private void NextAll(byte[] data) => _channel.Next(data);

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _receiveLoop?.Wait(1000);
            }
            catch { }

            _socket.Dispose();
            _cts.Dispose();
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
                        return new Unsubscriber(_observers, observer, _gate, active: false);
                    }

                    _observers.Add(observer);
                    return new Unsubscriber(_observers, observer, _gate, active: true);
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

            public void Error(Exception error)
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
                    _syncContext.Post(_ => o.OnError(error), null);
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
}

