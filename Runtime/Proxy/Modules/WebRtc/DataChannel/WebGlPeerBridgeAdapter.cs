using System;
using System.Runtime.InteropServices;
using System.Text;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;

namespace Playserv.Proxy.Implementation
{
    internal interface IWebRtcPeerBridgeAdapter : IDisposable
    {
        event Action Opened;
        event Action<string> MessageReceived;
        event Action<string> ErrorReceived;
        event Action<string> Closed;
        event Action<string> SignalReceived;

        void Connect(PlayServRuntimeSettings settings, IJsonCodec jsonCodec);
        bool Send(byte[] data);
        bool ApplySignal(string json);
        void Close();
    }

    internal sealed class WebGlPeerBridgeAdapter : IWebRtcPeerBridgeAdapter
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        private WebGLWebRtcBridge _bridge;
        private bool _subscribed;

        [DllImport("__Internal")]
        private static extern void Rtc_Connect(string gameObjectName, string configJson, string label);

        [DllImport("__Internal")]
        private static extern int Rtc_Send(string message);

        [DllImport("__Internal")]
        private static extern void Rtc_Close();

        [DllImport("__Internal")]
        private static extern int Rtc_ApplySignal(string signalJson);
#endif

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<string> Closed;
        public event Action<string> SignalReceived;

        public void Connect(PlayServRuntimeSettings settings, IJsonCodec jsonCodec)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            EnsureSubscriptions();

            var config = new
            {
                iceServers = settings.WebRtcIceServers ?? Array.Empty<string>()
            };

            var rtcConfigJson = jsonCodec.Serialize(config);
            var label = string.IsNullOrWhiteSpace(settings.WebRtcDataChannelLabel)
                ? PlayServRuntimeSettings.DefaultWebRtcDataChannelLabel
                : settings.WebRtcDataChannelLabel.Trim();

            Rtc_Connect(_bridge.GameObjectName, rtcConfigJson, label);
#else
            throw new PlatformNotSupportedException(
                "WebRTC DataChannel peer bridge is only implemented for WebGL JS bridge in this SDK slice.");
#endif
        }

        public bool Send(byte[] data)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var message = Encoding.UTF8.GetString(data ?? Array.Empty<byte>());
            return Rtc_Send(message) == 1;
#else
            throw new PlatformNotSupportedException(
                "WebRTC DataChannel peer bridge is only implemented for WebGL JS bridge in this SDK slice.");
#endif
        }

        public bool ApplySignal(string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Rtc_ApplySignal(json ?? string.Empty) == 1;
#else
            throw new PlatformNotSupportedException(
                "WebRTC DataChannel peer bridge is only implemented for WebGL JS bridge in this SDK slice.");
#endif
        }

        public void Close()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                Rtc_Close();
            }
            catch
            {
            }
#endif
        }

        public void Dispose()
        {
            Close();
            RemoveSubscriptions();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void EnsureSubscriptions()
        {
            if (_subscribed)
                return;

            _bridge = WebGLWebRtcBridge.Instance;
            _bridge.Opened += OnOpened;
            _bridge.MessageReceived += OnMessageReceived;
            _bridge.ErrorReceived += OnErrorReceived;
            _bridge.Closed += OnClosed;
            _bridge.SignalReceived += OnSignalReceived;
            _subscribed = true;
        }

        private void RemoveSubscriptions()
        {
            if (!_subscribed || _bridge == null)
                return;

            _bridge.Opened -= OnOpened;
            _bridge.MessageReceived -= OnMessageReceived;
            _bridge.ErrorReceived -= OnErrorReceived;
            _bridge.Closed -= OnClosed;
            _bridge.SignalReceived -= OnSignalReceived;
            _subscribed = false;
        }

        private void OnOpened()
        {
            Opened?.Invoke();
        }

        private void OnMessageReceived(string data)
        {
            MessageReceived?.Invoke(data);
        }

        private void OnErrorReceived(string error)
        {
            ErrorReceived?.Invoke(error);
        }

        private void OnClosed(string reason)
        {
            Closed?.Invoke(reason);
        }

        private void OnSignalReceived(string json)
        {
            SignalReceived?.Invoke(json);
        }
#else
        private void RemoveSubscriptions()
        {
        }
#endif
    }
}
