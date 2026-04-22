using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Proxy.WebRtc;
using Playserv.Wrapper;

namespace Playserv.Proxy.Implementation
{
    public sealed class WebRtcDataChannelTransportImplementation : ITransportImplementation
    {
        private const int ConnectTimeoutMs = 15000;

        private readonly string _endpoint;
        private readonly PlayServSettings _settings;
        private readonly IWebRtcSignalingClient _signalingClient;
        private readonly ILogger _logger;
        private readonly object _gate = new object();
        private readonly object _connectGate = new object();
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private readonly SynchronizationContext _syncContext;
        private ByteArrayChannel _channel;

        private bool _connecting;
        private bool _isConnected;
        private bool _isDisposed;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool _bridgeEventsSubscribed;
#endif
        private TaskCompletionSource<bool> _connectTcs;

#if UNITY_WEBGL && !UNITY_EDITOR
        private WebGLWebRtcBridge _bridge;

        [DllImport("__Internal")]
        private static extern void Rtc_Connect(string gameObjectName, string configJson, string label);

        [DllImport("__Internal")]
        private static extern int Rtc_Send(string message);

        [DllImport("__Internal")]
        private static extern void Rtc_Close();

        [DllImport("__Internal")]
        private static extern int Rtc_ApplySignal(string signalJson);
#endif

        public WebRtcDataChannelTransportImplementation(
            string endpoint,
            PlayServSettings settings,
            IWebRtcSignalingClient signalingClient,
            ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            _endpoint = endpoint;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _signalingClient = signalingClient;
            _logger = logger ?? new ConsoleLogger();
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ByteArrayChannel(_observers, _gate, _syncContext);

            if (_signalingClient != null)
            {
                _signalingClient.MessageReceived += OnSignalingMessageReceived;
                _signalingClient.ErrorReceived += OnSignalingErrorReceived;
            }
        }

        public async Task<bool> Connect()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WebRtcDataChannelTransportImplementation));

            if (_signalingClient == null)
            {
                _logger.LogError(
                    "WebRTC DataChannel transport requires signaling client factory. " +
                    "Call PlayServ.SetWebRtcSignalingClientFactory(...) before Connect().");
                return false;
            }

            TaskCompletionSource<bool> connectTcs = null;
            Task<bool> existingConnectTask = null;
            var alreadyConnected = false;
            lock (_connectGate)
            {
                if (_connecting)
                {
                    _logger.LogWarning("WebRTC DataChannel connect already in progress.");
                    existingConnectTask = _connectTcs?.Task ?? Task.FromResult(false);
                }
                else if (_isConnected)
                {
                    if (_channel.IsCompleted)
                    {
                        _logger.LogWarning("WebRTC DataChannel is marked connected but channel is completed. Recreating receive channel.");
                        _isConnected = false;
                        ResetChannel();
                    }
                    else
                    {
                        _logger.LogWarning("WebRTC DataChannel is already connected.");
                        alreadyConnected = true;
                    }
                }
                else if (_channel.IsCompleted)
                {
                    ResetChannel();
                }

                if (existingConnectTask == null && !alreadyConnected)
                {
                    _connecting = true;
                    _isConnected = false;
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
                var signalingConnected = await _signalingClient.Connect();
                if (!signalingConnected)
                {
                    _logger.LogError(
                        $"WebRTC signaling connect failed. endpoint={_endpoint}, signaling={_settings.WebRtcSignalingServerAddress}");
                    FailPendingConnect(connectTcs, completeChannel: false);
                    return false;
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                EnsureBridgeEventHandlers();
                var rtcConfigJson = BuildRtcConfigJson();
                var label = string.IsNullOrWhiteSpace(_settings.WebRtcDataChannelLabel)
                    ? PlayServSettings.DefaultWebRtcDataChannelLabel
                    : _settings.WebRtcDataChannelLabel.Trim();

                _logger.Log($"Connecting WebGL WebRTC DataChannel. endpoint={_endpoint}, signaling={_settings.WebRtcSignalingServerAddress}");
                Rtc_Connect(_bridge.GameObjectName, rtcConfigJson, label);
                _ = WatchConnectTimeoutAsync(connectTcs);
                return await connectTcs.Task;
#else
                _logger.LogError(
                    "WebRTC DataChannel runtime is only implemented for WebGL JS bridge in this SDK slice. " +
                    "Native Unity peer-connection implementation is not wired yet.");
                _signalingClient.Reset();
                FailPendingConnect(connectTcs, completeChannel: false);
                return false;
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError($"WebRTC DataChannel connect failed: {ex.Message}");
                FailPendingConnect(connectTcs, completeChannel: false);
                return false;
            }
        }

        public Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WebRtcDataChannelTransportImplementation));

            if (!_isConnected)
            {
                _logger.LogError("Attempted to send data but WebRTC DataChannel is not connected.");
                throw new InvalidOperationException("WebRTC DataChannel is not connected.");
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                var message = Encoding.UTF8.GetString(data);
                var sendResult = Rtc_Send(message);
                if (sendResult != 1)
                {
                    _logger.LogError("WebRTC DataChannel send failed: JS layer reported channel is not open.");
                    _isConnected = false;
                    CompleteAll();
                    TryClosePeer();
                    throw new InvalidOperationException("WebRTC DataChannel send failed (channel not open or JS send error).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send data via WebRTC DataChannel: {ex.Message}");
                _isConnected = false;
                CompleteAll();
                TryClosePeer();
                throw;
            }

            return Task.CompletedTask;
#else
            throw new PlatformNotSupportedException(
                "WebRTC DataChannel send is only implemented for WebGL JS bridge in this SDK slice.");
#endif
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
            _signalingClient?.Reset();
            TryClosePeer();
            ResetChannel();
            _logger.Log("WebRTC DataChannel transport connection state reset.");
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isConnected = false;
            _connecting = false;

            TryClosePeer();
            RemoveBridgeEventHandlers();
            _channel.Complete();

            if (_signalingClient != null)
            {
                _signalingClient.MessageReceived -= OnSignalingMessageReceived;
                _signalingClient.ErrorReceived -= OnSignalingErrorReceived;
                _signalingClient.Dispose();
            }
        }

        private void CompleteAll() => _channel.Complete();

        private void OnSignalingMessageReceived(WebRtcSignalMessage message)
        {
            if (message == null)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            _syncContext.Post(_ =>
            {
                try
                {
                    var json = JsonConvert.SerializeObject(message);
                    var accepted = Rtc_ApplySignal(json);
                    if (accepted != 1)
                    {
                        _logger.LogWarning(
                            $"WebRTC JS bridge did not accept remote signaling message. type={message.MessageType}, endpoint={_endpoint}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to forward remote signaling message to WebRTC JS bridge: {ex.Message}");
                }
            }, null);
#else
            _logger.LogWarning(
                $"Received WebRTC signaling message '{message.MessageType}' but native DataChannel runtime is not implemented.");
#endif
        }

        private void OnSignalingErrorReceived(Exception ex)
        {
            TaskCompletionSource<bool> pendingConnect;
            var wasConnected = false;
            lock (_connectGate)
            {
                wasConnected = _isConnected;
                _isConnected = false;
                _connecting = false;
                pendingConnect = _connectTcs;
                _connectTcs = null;
            }

            _logger.LogError($"WebRTC signaling error: {ex?.Message ?? "<null>"}");
            pendingConnect?.TrySetResult(false);
            _signalingClient?.Reset();
            TryClosePeer();

            if (wasConnected)
                CompleteAll();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void EnsureBridgeEventHandlers()
        {
            if (_bridgeEventsSubscribed)
                return;

            _bridge = WebGLWebRtcBridge.Instance;
            _bridge.Opened += OnBridgeOpened;
            _bridge.MessageReceived += OnBridgeMessageReceived;
            _bridge.ErrorReceived += OnBridgeErrorReceived;
            _bridge.Closed += OnBridgeClosed;
            _bridge.SignalReceived += OnBridgeSignalReceived;
            _bridgeEventsSubscribed = true;
        }

        private void OnBridgeOpened()
        {
            TaskCompletionSource<bool> pendingConnect;
            lock (_connectGate)
            {
                if (!_connecting)
                {
                    _logger.LogWarning("WebGL OnRtcOpen received without active connect attempt. Ignored.");
                    return;
                }

                _isConnected = true;
                _connecting = false;
                pendingConnect = _connectTcs;
                _connectTcs = null;
            }

            _logger.Log("WebGL WebRTC DataChannel connected successfully.");
            pendingConnect?.TrySetResult(true);
        }

        private void OnBridgeMessageReceived(string data)
        {
            if (data == null)
                return;

            _channel.Next(Encoding.UTF8.GetBytes(data));
        }

        private void OnBridgeSignalReceived(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            _ = ForwardLocalSignalAsync(json);
        }

        private async Task ForwardLocalSignalAsync(string json)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<WebRtcSignalMessage>(json);
                if (message == null)
                {
                    _logger.LogError($"Failed to deserialize local WebRTC signaling message. payload={json}");
                    return;
                }

                await _signalingClient.SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to forward local WebRTC signaling message: {ex.Message}");
            }
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

            _logger.LogError($"WebGL WebRTC DataChannel error: {error}");
            pendingConnect?.TrySetResult(false);
            _signalingClient?.Reset();
            TryClosePeer();

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

            _logger.LogWarning($"WebGL WebRTC DataChannel closed: {reason}");
            pendingConnect?.TrySetResult(false);
            _signalingClient?.Reset();
            TryClosePeer();

            if (wasConnected)
                CompleteAll();
        }
#endif

        private void RemoveBridgeEventHandlers()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_bridgeEventsSubscribed || _bridge == null)
                return;

            _bridge.Opened -= OnBridgeOpened;
            _bridge.MessageReceived -= OnBridgeMessageReceived;
            _bridge.ErrorReceived -= OnBridgeErrorReceived;
            _bridge.Closed -= OnBridgeClosed;
            _bridge.SignalReceived -= OnBridgeSignalReceived;
            _bridgeEventsSubscribed = false;
#endif
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

            _logger.LogWarning($"WebRTC DataChannel connect timed out after {ConnectTimeoutMs}ms.");
            TryClosePeer();
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

        private void FailPendingConnect(TaskCompletionSource<bool> connectTcs, bool completeChannel)
        {
            lock (_connectGate)
            {
                if (ReferenceEquals(_connectTcs, connectTcs))
                    _connectTcs = null;

                _connecting = false;
                _isConnected = false;
            }

            connectTcs?.TrySetResult(false);
            if (completeChannel)
                CompleteAll();
        }

        private string BuildRtcConfigJson()
        {
            var config = new
            {
                iceServers = _settings.WebRtcIceServers ?? Array.Empty<string>()
            };

            return JsonConvert.SerializeObject(config);
        }

        private void TryClosePeer()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                Rtc_Close();
            }
            catch
            {
                // ignored
            }
#endif
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

                foreach (var observer in snapshot)
                    _syncContext.Post(_ => observer.OnNext(value), null);
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
                    _syncContext.Post(_ => observer.OnCompleted(), null);
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
}
