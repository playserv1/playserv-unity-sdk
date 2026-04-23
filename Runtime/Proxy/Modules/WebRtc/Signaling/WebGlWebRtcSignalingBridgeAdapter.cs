using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.WebRtc
{
    internal sealed class WebGlWebRtcSignalingBridgeAdapter : IWebRtcSignalingConnection
    {
        private readonly Uri _uri;
        private readonly ILogger _logger;

#if UNITY_WEBGL && !UNITY_EDITOR
        private WebGLWebRtcSignalingBridge _bridge;
        private bool _bridgeEventsSubscribed;

        [DllImport("__Internal")]
        private static extern void RtcSig_Connect(string gameObjectName, string url);

        [DllImport("__Internal")]
        private static extern int RtcSig_Send(string message);

        [DllImport("__Internal")]
        private static extern void RtcSig_Close();
#endif

        public WebGlWebRtcSignalingBridgeAdapter(Uri uri, ILogger logger)
        {
            _uri = uri ?? throw new ArgumentNullException(nameof(uri));
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
        }

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<Exception> ErrorReceived;
        public event Action<string> Closed;

        public Task Connect(CancellationToken ct)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureBridgeEventHandlers();
            RtcSig_Connect(_bridge.GameObjectName, _uri.ToString());
            return Task.CompletedTask;
#else
            throw new PlatformNotSupportedException("WebGL signaling bridge adapter is only available in WebGL builds.");
#endif
        }

        public Task SendAsync(string message, CancellationToken ct)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var sendResult = RtcSig_Send(message ?? string.Empty);
            if (sendResult != 1)
                throw new InvalidOperationException("WebRTC signaling websocket send failed.");

            return Task.CompletedTask;
#else
            throw new PlatformNotSupportedException("WebGL signaling bridge adapter is only available in WebGL builds.");
#endif
        }

        public void Reset()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                RtcSig_Close();
            }
            catch
            {
            }
#endif
        }

        public void Dispose()
        {
            Reset();
            RemoveBridgeEventHandlers();
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
            Opened?.Invoke();
        }

        private void OnBridgeMessageReceived(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            MessageReceived?.Invoke(json);
        }

        private void OnBridgeErrorReceived(string error)
        {
            var ex = new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "WebRTC signaling websocket error." : error);
            ErrorReceived?.Invoke(ex);
        }

        private void OnBridgeClosed(string reason)
        {
            Closed?.Invoke(reason);
        }
#else
        private void RemoveBridgeEventHandlers()
        {
        }
#endif
    }
}
