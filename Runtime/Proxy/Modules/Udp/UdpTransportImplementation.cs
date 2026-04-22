using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
#if !UNITY_WEBGL || UNITY_EDITOR
    /// <summary>
    /// UDP datagram transport for backend endpoints exposed via <c>udp://host:port</c>.
    /// Uses the same raw JSON frame contract as other PlayServ transports.
    /// </summary>
    public sealed class UdpTransportImplementation : ITransportImplementation
    {
        private const string UdpScheme = "udp";

        private readonly Uri _uri;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private readonly object _connectGate = new object();
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private readonly SynchronizationContext _syncContext;
        private ByteArrayChannel _channel;

        private UdpClient _client;
        private Task _receiveLoop;
        private bool _connected;
        private bool _connecting;

        public UdpTransportImplementation(string uri, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new ArgumentException("UDP endpoint cannot be null or empty.", nameof(uri));

            _uri = new Uri(uri);
            if (!string.Equals(_uri.Scheme, UdpScheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"UDP transport requires '{UdpScheme}://' endpoint. Actual scheme: '{_uri.Scheme}'.",
                    nameof(uri));
            }

            if (_uri.Port <= 0)
                throw new ArgumentException("UDP endpoint must include an explicit port.", nameof(uri));

            _logger = logger ?? new ConsoleLogger();
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ByteArrayChannel(_observers, _gate, _syncContext);
        }

        public Task<bool> Connect()
        {
            lock (_connectGate)
            {
                if (_connecting)
                {
                    _logger.LogWarning("UDP connect already in progress.");
                    return Task.FromResult(false);
                }

                if (_connected)
                {
                    if (_channel.IsCompleted)
                    {
                        _logger.LogWarning("UDP transport is marked connected but receive channel is completed. Recreating client/channel.");
                        CloseClientUnsafe();
                        _connected = false;
                        ResetChannel();
                    }
                    else
                    {
                        _logger.LogWarning("UDP transport is already connected.");
                        return Task.FromResult(true);
                    }
                }
                else
                {
                    CloseClientUnsafe();
                    if (_channel.IsCompleted)
                        ResetChannel();
                }

                _connecting = true;
            }

            try
            {
                var client = new UdpClient();
                client.Connect(_uri.Host, _uri.Port);

                lock (_connectGate)
                {
                    _client = client;
                    _connected = true;
                }

                _receiveLoop = Task.Run(() => ReceiveLoop(client));
                _logger.Log($"UDP transport ready: {_uri}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize UDP transport: {ex.Message}");
                CloseClientUnsafe();
                _connected = false;
                return Task.FromResult(false);
            }
            finally
            {
                lock (_connectGate)
                {
                    _connecting = false;
                }
            }
        }

        public async Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            UdpClient client;
            lock (_connectGate)
            {
                client = _client;
                if (!_connected || client == null)
                {
                    _logger.LogError("Attempted to send data but UDP transport is not connected.");
                    throw new InvalidOperationException("UDP transport is not connected.");
                }
            }

            try
            {
                await client.SendAsync(data, data.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send data via UDP transport: {ex.Message}");
                throw;
            }
        }

        public IObservable<byte[]> OnReceive() => _channel;

        public void ResetConnection()
        {
            lock (_connectGate)
            {
                _connecting = false;
                _connected = false;
                CloseClientUnsafe();
                ResetChannel();
            }

            _logger.Log("[UDP] Transport connection state reset.");
        }

        public void Dispose()
        {
            _cts.Cancel();
            CloseClientUnsafe();

            try
            {
                _receiveLoop?.Wait(1000);
            }
            catch
            {
                // ignored
            }

            _cts.Dispose();
        }

        private async Task ReceiveLoop(UdpClient client)
        {
            ByteArrayChannel channel;
            lock (_gate)
            {
                channel = _channel;
            }

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync();
                    if (result.Buffer == null || result.Buffer.Length == 0)
                    {
                        _logger.LogWarning("Received empty UDP datagram.");
                        continue;
                    }

                    channel.Next(result.Buffer);
                }
            }
            catch (ObjectDisposedException)
            {
                if (_cts.IsCancellationRequested)
                    channel.Complete();
            }
            catch (SocketException ex)
            {
                if (_cts.IsCancellationRequested)
                {
                    channel.Complete();
                    return;
                }

                _logger.LogError($"Error in UDP receive loop: {ex.Message}");
                channel.Error(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in UDP receive loop: {ex.Message}");
                channel.Error(ex);
            }
        }

        private void ResetChannel()
        {
            lock (_gate)
            {
                _observers.Clear();
                _channel = new ByteArrayChannel(_observers, _gate, _syncContext);
            }
        }

        private void CloseClientUnsafe()
        {
            try
            {
                _client?.Close();
            }
            catch
            {
                // ignored
            }

            try
            {
                _client?.Dispose();
            }
            catch
            {
                // ignored
            }

            _client = null;
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

                foreach (var observer in snapshot)
                {
                    _syncContext.Post(_ => observer.OnNext(value), null);
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

                foreach (var observer in snapshot)
                {
                    _syncContext.Post(_ => observer.OnError(error), null);
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

                foreach (var observer in snapshot)
                {
                    _syncContext.Post(_ => observer.OnCompleted(), null);
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
