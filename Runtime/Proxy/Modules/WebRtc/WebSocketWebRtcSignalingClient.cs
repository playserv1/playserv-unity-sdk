using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Logging;
using Playserv.Wrapper;

namespace Playserv.Proxy.WebRtc
{
    /// <summary>
    /// Default WebRTC signaling client implementation based on a websocket signaling server.
    /// </summary>
    public sealed class WebSocketWebRtcSignalingClient : IWebRtcSignalingClient
    {
        private const int ConnectTimeoutMs = 12000;
        private const string WebSocketScheme = "ws";
        private const string SecureWebSocketScheme = "wss";
        private const string HttpScheme = "http";
        private const string HttpsScheme = "https";

        private readonly PlayServSettings _settings;
        private readonly ILogger _logger;
        private readonly Uri _uri;
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private readonly object _connectGate = new object();

#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private ClientWebSocket _socket = new ClientWebSocket();
        private Task _receiveLoop;
        private CancellationTokenSource _connectCts;
#else
        private WebGLWebRtcSignalingBridge _bridge;

        [DllImport("__Internal")]
        private static extern void RtcSig_Connect(string gameObjectName, string url);

        [DllImport("__Internal")]
        private static extern int RtcSig_Send(string message);

        [DllImport("__Internal")]
        private static extern void RtcSig_Close();
#endif

        private TaskCompletionSource<bool> _connectTcs;
        private bool _connecting;
        private bool _connected;
        private bool _disposed;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool _bridgeEventsSubscribed;
#endif

        public WebSocketWebRtcSignalingClient(PlayServSettings settings, ILogger logger = null)
        {
            _settings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? new ConsoleLogger();

            if (string.IsNullOrWhiteSpace(_settings.WebRtcSignalingServerAddress))
            {
                throw new InvalidOperationException(
                    "PlayServSettings.WebRtcSignalingServerAddress is required for default WebRTC signaling client.");
            }

            _uri = BuildWebSocketUri(_settings.WebRtcSignalingServerAddress);
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

#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureBridgeEventHandlers();

            try
            {
                RtcSig_Connect(_bridge.GameObjectName, _uri.ToString());
            }
            catch (Exception ex)
            {
                FailConnect(connectTcs, ex);
            }

            _ = WatchConnectTimeoutAsync(connectTcs);
            return await connectTcs.Task;
#else
            try
            {
                _socket?.Dispose();
                _socket = new ClientWebSocket();
                _connectCts?.Dispose();
                _connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);

                _logger.Log($"Connecting WebRTC signaling websocket: {_uri}");
                await _socket.ConnectAsync(_uri, _connectCts.Token);
                _receiveLoop = Task.Run(ReceiveLoop);

                await SendHelloAsync(ct);

                lock (_connectGate)
                {
                    _connected = true;
                    _connecting = false;
                    if (ReferenceEquals(_connectTcs, connectTcs))
                        _connectTcs = null;
                }

                connectTcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                FailConnect(connectTcs, ex);
            }

            return await connectTcs.Task;
#endif
        }

        public async Task SendAsync(WebRtcSignalMessage message, CancellationToken ct = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketWebRtcSignalingClient));

            if (!_connected)
                throw new InvalidOperationException("WebRTC signaling websocket is not connected.");

            if (string.IsNullOrWhiteSpace(message.SessionId))
                message.SessionId = _sessionId;

            var json = JsonConvert.SerializeObject(message);

#if UNITY_WEBGL && !UNITY_EDITOR
            var sendResult = RtcSig_Send(json);
            if (sendResult != 1)
                throw new InvalidOperationException("WebRTC signaling websocket send failed.");

            await Task.CompletedTask;
#else
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);
            await _socket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
#endif
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

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                RtcSig_Close();
            }
            catch
            {
                // ignored
            }
#else
            try
            {
                _connectCts?.Cancel();
            }
            catch
            {
                // ignored
            }

            try
            {
                _socket?.Abort();
            }
            catch
            {
                // ignored
            }

            try
            {
                _socket?.Dispose();
            }
            catch
            {
                // ignored
            }

            _socket = new ClientWebSocket();
#endif
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Reset();

#if UNITY_WEBGL && !UNITY_EDITOR
            RemoveBridgeEventHandlers();
#else
            _disposeCts.Cancel();
            try
            {
                _receiveLoop?.Wait(1000);
            }
            catch
            {
                // ignored
            }

            _connectCts?.Dispose();
            _socket?.Dispose();
            _disposeCts.Dispose();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void EnsureBridgeEventHandlers()
        {
            if (_bridgeEventsSubscribed)
                return;

            _bridge = WebGLWebRtcSignalingBridge.Instance;
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
                    return;

                pendingConnect = _connectTcs;
            }

            _ = FinishWebGlConnectAsync(pendingConnect);
        }

        private async Task FinishWebGlConnectAsync(TaskCompletionSource<bool> pendingConnect)
        {
            try
            {
                var hello = BuildHelloMessage();
                var sendResult = RtcSig_Send(hello);
                if (sendResult != 1)
                    throw new InvalidOperationException("Failed to send WebRTC signaling hello message.");

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

        private void OnBridgeMessageReceived(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            TryDispatchMessage(json);
        }

        private void OnBridgeErrorReceived(string error)
        {
            var ex = new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "WebRTC signaling websocket error." : error);
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

        private void OnBridgeClosed(string reason)
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
                _connected = false;
                _connectTcs = null;
            }

            try
            {
                RtcSig_Close();
            }
            catch
            {
                // ignored
            }

            connectTcs.TrySetResult(false);
        }
#else
        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            using var ms = new MemoryStream();

            try
            {
                while (!_disposeCts.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _socket.ReceiveAsync(segment, _disposeCts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        lock (_connectGate)
                        {
                            _connected = false;
                            _connecting = false;
                        }

                        ErrorReceived?.Invoke(new InvalidOperationException(
                            $"WebRTC signaling websocket closed: {_socket.CloseStatus} {_socket.CloseStatusDescription}"));
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);
                    TryDispatchMessage(json);
                }
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(ex);
            }
        }

        private async Task SendHelloAsync(CancellationToken ct)
        {
            var json = BuildHelloMessage();
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);
            await _socket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
        }
#endif

        private void TryDispatchMessage(string json)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<WebRtcSignalMessage>(json);
                if (message == null)
                    return;

                if (!string.IsNullOrWhiteSpace(message.SessionId) &&
                    !string.Equals(message.SessionId, _sessionId, StringComparison.Ordinal))
                {
                    return;
                }

                MessageReceived?.Invoke(message);
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(new InvalidOperationException(
                    $"Failed to parse WebRTC signaling message. Payload={json}. Error={ex.Message}", ex));
            }
        }

        private string BuildHelloMessage()
        {
            var hello = new WebRtcSignalingHelloMessage
            {
                MessageType = WebRtcSignalMessageTypes.Hello,
                SessionId = _sessionId,
                GameAccessToken = _settings.GameAccessToken ?? string.Empty,
                GameId = _settings.GameId ?? string.Empty,
                UserId = _settings.UserId ?? string.Empty,
                GameVersion = _settings.GameVersion ?? string.Empty,
                SdkVersion = _settings.SdkVersion ?? string.Empty,
                ChannelLabel = _settings.WebRtcDataChannelLabel ?? string.Empty,
                TransportEndpoint = _settings.BackendServerAddress ?? string.Empty
            };

            return JsonConvert.SerializeObject(hello);
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

        private static Uri BuildWebSocketUri(string address)
        {
            var value = (address ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("WebRTC signaling server address is empty.");

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"Invalid WebRTC signaling server address: {value}");

            if (string.Equals(uri.Scheme, HttpScheme, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri) { Scheme = WebSocketScheme, Port = uri.IsDefaultPort ? 80 : uri.Port };
                return builder.Uri;
            }

            if (string.Equals(uri.Scheme, HttpsScheme, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri) { Scheme = SecureWebSocketScheme, Port = uri.IsDefaultPort ? 443 : uri.Port };
                return builder.Uri;
            }

            if (string.Equals(uri.Scheme, WebSocketScheme, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, SecureWebSocketScheme, StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            throw new InvalidOperationException(
                $"Unsupported WebRTC signaling scheme '{uri.Scheme}'. Use ws://, wss://, http:// or https://.");
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

        private sealed class WebRtcSignalingHelloMessage
        {
            public string MessageType { get; set; }
            public string SessionId { get; set; }
            public string GameAccessToken { get; set; }
            public string GameId { get; set; }
            public string UserId { get; set; }
            public string GameVersion { get; set; }
            public string SdkVersion { get; set; }
            public string ChannelLabel { get; set; }
            public string TransportEndpoint { get; set; }
        }
    }
}
