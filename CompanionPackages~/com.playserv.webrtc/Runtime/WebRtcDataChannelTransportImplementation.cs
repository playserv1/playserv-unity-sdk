using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    public sealed class WebRtcDataChannelTransportImplementation : ITransportImplementation
    {
        private const int ConnectTimeoutMs = 15000;

        private readonly string _endpoint;
        private readonly PlayServRuntimeSettings _settings;
        private readonly IWebRtcSignalingClient _signalingClient;
        private readonly IJsonCodec _jsonCodec;
        private readonly ILogger _logger;
        private readonly WebRtcDataChannelSession _session;
        private readonly IWebRtcPeerBridgeAdapter _peerBridge;
        private readonly WebRtcSignalForwarder _signalForwarder;
        private readonly WebRtcConnectTimeoutWatcher _connectTimeoutWatcher;

        public WebRtcDataChannelTransportImplementation(
            string endpoint,
            PlayServRuntimeSettings settings,
            IWebRtcSignalingClient signalingClient,
            IJsonCodec jsonCodec,
            ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            _endpoint = endpoint;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _signalingClient = signalingClient;
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));
            _jsonCodec = jsonCodec;
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
            var syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _session = new WebRtcDataChannelSession(syncContext);
            _peerBridge = new WebGlPeerBridgeAdapter();
            _signalForwarder = new WebRtcSignalForwarder(
                _signalingClient,
                jsonCodec,
                _peerBridge,
                _logger,
                syncContext,
                _endpoint);
            _connectTimeoutWatcher = new WebRtcConnectTimeoutWatcher(ConnectTimeoutMs, _logger, "WebRTC DataChannel");

            if (_signalingClient != null)
            {
                _signalingClient.MessageReceived += OnSignalingMessageReceived;
                _signalingClient.ErrorReceived += OnSignalingErrorReceived;
            }

            _peerBridge.Opened += OnBridgeOpened;
            _peerBridge.MessageReceived += OnBridgeMessageReceived;
            _peerBridge.ErrorReceived += OnBridgeErrorReceived;
            _peerBridge.Closed += OnBridgeClosed;
            _peerBridge.SignalReceived += OnBridgeSignalReceived;
        }

        public async Task<bool> Connect()
        {
            _session.ThrowIfDisposed(nameof(WebRtcDataChannelTransportImplementation));

            if (_signalingClient == null)
            {
                _logger.LogError(
                    "WebRTC DataChannel transport requires configured signaling client integration " +
                    "before Connect().");
                return false;
            }

            TaskCompletionSource<bool> connectTcs;
            Task<bool> existingConnectTask;
            bool alreadyConnected;
            _session.BeginConnect(_logger, out existingConnectTask, out connectTcs, out alreadyConnected);

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
                    _session.FailPendingConnect(connectTcs, completeChannel: false);
                    return false;
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                _logger.Log($"Connecting WebGL WebRTC DataChannel. endpoint={_endpoint}, signaling={_settings.WebRtcSignalingServerAddress}");
                _peerBridge.Connect(_settings, _jsonCodec);
                _ = _connectTimeoutWatcher.WatchAsync(connectTcs, _session.TryTimeout, _peerBridge.Close);
                return await connectTcs.Task;
#else
                _logger.LogError(
                    "WebRTC DataChannel runtime is only implemented for WebGL JS bridge in this SDK slice. " +
                    "Native Unity peer-connection implementation is not wired yet.");
                _signalingClient.Reset();
                _session.FailPendingConnect(connectTcs, completeChannel: false);
                return false;
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError($"WebRTC DataChannel connect failed: {ex.Message}");
                _session.FailPendingConnect(connectTcs, completeChannel: false);
                return false;
            }
        }

        public Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            _session.ThrowIfDisposed(nameof(WebRtcDataChannelTransportImplementation));

            if (!_session.IsConnected)
            {
                _logger.LogError("Attempted to send data but WebRTC DataChannel is not connected.");
                throw new InvalidOperationException("WebRTC DataChannel is not connected.");
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                if (!_peerBridge.Send(data))
                {
                    _logger.LogError("WebRTC DataChannel send failed: JS layer reported channel is not open.");
                    TaskCompletionSource<bool> pendingConnect;
                    bool wasConnected;
                    _session.TryDeactivate(out pendingConnect, out wasConnected, ignoreIfInactive: false);
                    if (wasConnected)
                        _session.Complete();
                    _peerBridge.Close();
                    throw new InvalidOperationException("WebRTC DataChannel send failed (channel not open or JS send error).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send data via WebRTC DataChannel: {ex.Message}");
                TaskCompletionSource<bool> pendingConnect;
                bool wasConnected;
                _session.TryDeactivate(out pendingConnect, out wasConnected, ignoreIfInactive: false);
                if (wasConnected)
                    _session.Complete();
                _peerBridge.Close();
                throw;
            }

            return Task.CompletedTask;
#else
            throw new PlatformNotSupportedException(
                "WebRTC DataChannel send is only implemented for WebGL JS bridge in this SDK slice.");
#endif
        }

        public IObservable<byte[]> OnReceive() => _session.OnReceive();

        public void ResetConnection()
        {
            if (_session.IsDisposed)
                return;

            TaskCompletionSource<bool> pendingConnect;
            _session.Reset(out pendingConnect);
            pendingConnect?.TrySetResult(false);
            _signalingClient?.Reset();
            _peerBridge.Close();
            _logger.Log("WebRTC DataChannel transport connection state reset.");
        }

        public void Dispose()
        {
            if (_session.IsDisposed)
                return;

            _session.MarkDisposed();
            _peerBridge.Dispose();
            _session.Complete();

            if (_signalingClient != null)
            {
                _signalingClient.MessageReceived -= OnSignalingMessageReceived;
                _signalingClient.ErrorReceived -= OnSignalingErrorReceived;
                    _signalingClient.Dispose();
            }
        }

        private void OnSignalingMessageReceived(WebRtcSignalMessage message)
        {
            if (message == null)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            _signalForwarder.ForwardRemoteSignal(message);
#else
            _logger.LogWarning(
                $"Received WebRTC signaling message '{message.MessageType}' but native DataChannel runtime is not implemented.");
#endif
        }

        private void OnSignalingErrorReceived(Exception ex)
        {
            TaskCompletionSource<bool> pendingConnect;
            bool wasConnected;
            _session.TryDeactivate(out pendingConnect, out wasConnected, ignoreIfInactive: false);

            _logger.LogError($"WebRTC signaling error: {ex?.Message ?? "<null>"}");
            pendingConnect?.TrySetResult(false);
            _signalingClient?.Reset();
            _peerBridge.Close();

            if (wasConnected)
                _session.Complete();
        }

        private void OnBridgeOpened()
        {
            TaskCompletionSource<bool> pendingConnect;
            if (!_session.TryCompleteConnect(out pendingConnect))
            {
                _logger.LogWarning("WebGL OnRtcOpen received without active connect attempt. Ignored.");
                return;
            }

            _logger.Log("WebGL WebRTC DataChannel connected successfully.");
            pendingConnect?.TrySetResult(true);
        }

        private void OnBridgeMessageReceived(string data)
        {
            if (data == null)
                return;

            _session.Next(Encoding.UTF8.GetBytes(data));
        }

        private void OnBridgeSignalReceived(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            _ = _signalForwarder.ForwardLocalSignalAsync(json);
        }

        private void OnBridgeErrorReceived(string error)
        {
            TaskCompletionSource<bool> pendingConnect;
            bool wasConnected;
            if (!_session.TryDeactivate(out pendingConnect, out wasConnected, ignoreIfInactive: true))
                return;

            _logger.LogError($"WebGL WebRTC DataChannel error: {error}");
            pendingConnect?.TrySetResult(false);
            _signalingClient?.Reset();
            _peerBridge.Close();

            if (wasConnected)
                _session.Complete();
        }

        private void OnBridgeClosed(string reason)
        {
            TaskCompletionSource<bool> pendingConnect;
            bool wasConnected;
            if (!_session.TryDeactivate(out pendingConnect, out wasConnected, ignoreIfInactive: true))
                return;

            _logger.LogWarning($"WebGL WebRTC DataChannel closed: {reason}");
            pendingConnect?.TrySetResult(false);
            _signalingClient?.Reset();
            _peerBridge.Close();

            if (wasConnected)
                _session.Complete();
        }
    }
}
