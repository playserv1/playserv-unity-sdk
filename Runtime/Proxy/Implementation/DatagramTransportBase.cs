using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
#if !UNITY_WEBGL || UNITY_EDITOR
    internal abstract class DatagramTransportBase : ITransportImplementation
    {
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private readonly object _channelGate = new object();
        private readonly object _connectGate = new object();
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private ObservableByteChannel _channel;
        private UdpClient _client;
        private Task _receiveLoop;
        private bool _connected;
        private bool _connecting;

        protected DatagramTransportBase(
            string uri,
            string expectedScheme,
            string transportName,
            ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new ArgumentException($"{transportName} endpoint cannot be null or empty.", nameof(uri));

            EndpointUri = new Uri(uri);
            if (!string.Equals(EndpointUri.Scheme, expectedScheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{transportName} transport requires '{expectedScheme}://' endpoint. Actual scheme: '{EndpointUri.Scheme}'.",
                    nameof(uri));
            }

            if (EndpointUri.Port <= 0)
                throw new ArgumentException($"{transportName} endpoint must include an explicit port.", nameof(uri));

            TransportName = transportName;
            Logger = logger ?? new ConsoleLogger();
            SyncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = CreateChannel();
        }

        protected Uri EndpointUri { get; }
        protected string TransportName { get; }
        protected ILogger Logger { get; }
        protected CancellationToken DisposeToken => _disposeCts.Token;
        protected SynchronizationContext SyncContext { get; }

        public Task<bool> Connect()
        {
            lock (_connectGate)
            {
                if (_connecting)
                {
                    Logger.LogWarning($"{TransportName} connect already in progress.");
                    return Task.FromResult(false);
                }

                if (_connected)
                {
                    if (_channel.IsCompleted)
                    {
                        Logger.LogWarning(
                            $"{TransportName} transport is marked connected but receive channel is completed. Recreating client/channel.");
                        CloseClientUnsafe();
                        _connected = false;
                        ResetChannel();
                    }
                    else
                    {
                        Logger.LogWarning($"{TransportName} transport is already connected.");
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
                var client = CreateConnectedClient();

                lock (_connectGate)
                {
                    _client = client;
                    _connected = true;
                }

                OnConnected(client);
                var channel = CaptureChannel();
                _receiveLoop = Task.Run(() => ReceiveLoop(client, channel));
                Logger.Log($"{TransportName} transport ready: {EndpointUri}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize {TransportName} transport: {ex.Message}");
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

        public abstract Task Send(byte[] data);

        public IObservable<byte[]> OnReceive() => _channel;

        public void ResetConnection()
        {
            lock (_connectGate)
            {
                _connecting = false;
                _connected = false;
                CloseClientUnsafe();
                OnResetConnection();
                ResetChannel();
            }

            Logger.Log($"[{TransportName}] Transport connection state reset.");
        }

        public void Dispose()
        {
            _disposeCts.Cancel();
            CloseClientUnsafe();
            OnDispose();

            try
            {
                _receiveLoop?.Wait(1000);
            }
            catch
            {
                // ignored
            }

            _disposeCts.Dispose();
        }

        protected UdpClient GetConnectedClient()
        {
            lock (_connectGate)
            {
                var client = _client;
                if (!_connected || client == null)
                {
                    Logger.LogError($"Attempted to send data but {TransportName} transport is not connected.");
                    throw new InvalidOperationException($"{TransportName} transport is not connected.");
                }

                return client;
            }
        }

        protected void MarkDisconnectedAndCloseClient()
        {
            lock (_connectGate)
            {
                _connected = false;
                CloseClientUnsafe();
            }
        }

        protected virtual void OnConnected(UdpClient client)
        {
        }

        protected virtual void OnResetConnection()
        {
        }

        protected virtual void OnDispose()
        {
        }

        protected abstract Task ProcessReceivedDatagramAsync(
            UdpClient client,
            byte[] buffer,
            ObservableByteChannel channel);

        private async Task ReceiveLoop(UdpClient client, ObservableByteChannel channel)
        {
            try
            {
                while (!_disposeCts.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync();
                    await ProcessReceivedDatagramAsync(client, result.Buffer, channel);
                }
            }
            catch (ObjectDisposedException)
            {
                if (_disposeCts.IsCancellationRequested)
                    channel.Complete();
            }
            catch (SocketException ex)
            {
                if (_disposeCts.IsCancellationRequested)
                {
                    channel.Complete();
                    return;
                }

                Logger.LogError($"Error in {TransportName} receive loop: {ex.Message}");
                channel.Error(ex);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in {TransportName} receive loop: {ex.Message}");
                channel.Error(ex);
            }
        }

        private UdpClient CreateConnectedClient()
        {
            var client = new UdpClient();
            client.Connect(EndpointUri.Host, EndpointUri.Port);
            return client;
        }

        private ObservableByteChannel CaptureChannel()
        {
            lock (_channelGate)
            {
                return _channel;
            }
        }

        private ObservableByteChannel CreateChannel()
        {
            return new ObservableByteChannel(_observers, _channelGate, SyncContext);
        }

        private void ResetChannel()
        {
            lock (_channelGate)
            {
                _observers.Clear();
                _channel = CreateChannel();
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
    }
#endif
}
