using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

namespace Playserv.Proxy.WebRtc
{
    /// <summary>
    /// Default WebRTC signaling client implementation based on a websocket signaling server.
    /// </summary>
    public sealed class WebSocketWebRtcSignalingClient : IWebRtcSignalingClient
    {
        private const int ConnectTimeoutMs = 12000;

        private readonly ILogger _logger;
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private readonly object _connectGate = new object();
        private readonly IWebRtcSignalingConnection _connection;
        private readonly WebRtcSignalingProtocol _protocol;
        private readonly WebRtcSignalingHelloBuilder _helloBuilder;

        private TaskCompletionSource<bool> _connectTcs;
        private bool _connecting;
        private bool _connected;
        private bool _disposed;

        public WebSocketWebRtcSignalingClient(PlayServRuntimeSettings settings, IJsonCodec jsonCodec, ILogger logger = null)
        {
            var runtimeSettings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);

            if (string.IsNullOrWhiteSpace(runtimeSettings.WebRtcSignalingServerAddress))
            {
                throw new InvalidOperationException(
                    "PlayServRuntimeSettings.WebRtcSignalingServerAddress is required for default WebRTC signaling client.");
            }

            var uri = WebRtcSignalingConnection.BuildWebSocketUri(runtimeSettings.WebRtcSignalingServerAddress);
            _protocol = new WebRtcSignalingProtocol(jsonCodec, _sessionId);
            _helloBuilder = new WebRtcSignalingHelloBuilder(runtimeSettings, jsonCodec, _sessionId);
            _connection = WebRtcSignalingConnection.Create(uri, _logger);
            _connection.Opened += OnConnectionOpened;
            _connection.MessageReceived += OnConnectionMessageReceived;
            _connection.ErrorReceived += OnConnectionErrorReceived;
            _connection.Closed += OnConnectionClosed;
        }

        public event Action<WebRtcSignalMessage> MessageReceived;
        public event Action<Exception> ErrorReceived;

        public async Task<bool> Connect(CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketWebRtcSignalingClient));

            TaskCompletionSource<bool> connectTcs = null;
            Task<bool> existingConnectTask = null;
            var alreadyConnected = false;
            lock (_connectGate)
            {
                if (_connecting)
                {
                    existingConnectTask = _connectTcs?.Task ?? Task.FromResult(false);
                }

                else if (_connected)
                {
                    alreadyConnected = true;
                }
                else
                {
                    _connecting = true;
                    _connectTcs = new TaskCompletionSource<bool>();
                    connectTcs = _connectTcs;
                }
            }

            if (existingConnectTask != null)
                return await existingConnectTask;

            if (alreadyConnected)
                return true;

            try
            {
                _ = WatchConnectTimeoutAsync(connectTcs);
                await _connection.Connect(ct);
            }
            catch (Exception ex)
            {
                FailConnect(connectTcs, ex);
            }

            return await connectTcs.Task;
        }

        public async Task SendAsync(WebRtcSignalMessage message, CancellationToken ct = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketWebRtcSignalingClient));

            if (!_connected)
                throw new InvalidOperationException("WebRTC signaling websocket is not connected.");

            var json = _protocol.Serialize(message);
            await _connection.SendAsync(json, ct);
        }

        public void Reset()
        {
            if (_disposed)
                return;

            lock (_connectGate)
            {
                _connecting = false;
                _connected = false;
                _connectTcs = null;
            }
            _connection.Reset();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Reset();
            _connection.Opened -= OnConnectionOpened;
            _connection.MessageReceived -= OnConnectionMessageReceived;
            _connection.ErrorReceived -= OnConnectionErrorReceived;
            _connection.Closed -= OnConnectionClosed;
            _connection.Dispose();
        }

        private void OnConnectionOpened()
        {
            TaskCompletionSource<bool> pendingConnect;
            lock (_connectGate)
            {
                if (!_connecting)
                    return;

                pendingConnect = _connectTcs;
            }

            _ = FinishConnectAsync(pendingConnect);
        }

        private async Task FinishConnectAsync(TaskCompletionSource<bool> pendingConnect)
        {
            try
            {
                var hello = await _helloBuilder.BuildAsync(CancellationToken.None);
                await _connection.SendAsync(hello, CancellationToken.None);

                lock (_connectGate)
                {
                    _connected = true;
                    _connecting = false;
                    if (ReferenceEquals(_connectTcs, pendingConnect))
                        _connectTcs = null;
                }

                pendingConnect?.TrySetResult(true);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                FailConnect(pendingConnect, ex);
            }
        }

        private void OnConnectionMessageReceived(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            if (_protocol.TryDeserializeIncoming(json, out var message, out var error))
            {
                MessageReceived?.Invoke(message);
                return;
            }

            if (error != null)
                ErrorReceived?.Invoke(error);
        }

        private void OnConnectionErrorReceived(Exception ex)
        {
            var wasConnected = false;
            TaskCompletionSource<bool> pendingConnect;

            lock (_connectGate)
            {
                wasConnected = _connected;
                pendingConnect = _connectTcs;
                _connectTcs = null;
                _connecting = false;
                _connected = false;
            }

            pendingConnect?.TrySetResult(false);
            ErrorReceived?.Invoke(ex);

            if (wasConnected)
                Reset();
        }

        private void OnConnectionClosed(string reason)
        {
            var ex = new InvalidOperationException(string.IsNullOrWhiteSpace(reason) ? "WebRTC signaling websocket closed." : reason);
            var wasConnected = false;
            TaskCompletionSource<bool> pendingConnect;

            lock (_connectGate)
            {
                wasConnected = _connected;
                pendingConnect = _connectTcs;
                _connectTcs = null;
                _connecting = false;
                _connected = false;
            }

            pendingConnect?.TrySetResult(false);
            if (wasConnected)
                ErrorReceived?.Invoke(ex);
        }

        private void FailConnect(TaskCompletionSource<bool> connectTcs, Exception ex)
        {
            lock (_connectGate)
            {
                if (ReferenceEquals(_connectTcs, connectTcs))
                    _connectTcs = null;

                _connecting = false;
                _connected = false;
            }

            connectTcs?.TrySetResult(false);
            ErrorReceived?.Invoke(ex);
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
                _connected = false;
                _connectTcs = null;
            }

            _connection.Reset();
            connectTcs.TrySetResult(false);
        }
    }
}
