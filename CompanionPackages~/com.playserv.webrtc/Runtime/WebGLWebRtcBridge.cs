#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using UnityEngine;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebGLWebRtcBridge : MonoBehaviour
    {
        private static WebGLWebRtcBridge _instance;

        public static WebGLWebRtcBridge Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var go = new GameObject("WebGLWebRtcBridge");
                _instance = go.AddComponent<WebGLWebRtcBridge>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<string> Closed;
        public event Action<string> SignalReceived;

        public string GameObjectName => gameObject.name;

        public void OnRtcOpen(string _)
        {
            Opened?.Invoke();
        }

        public void OnRtcMessage(string data)
        {
            MessageReceived?.Invoke(data);
        }

        public void OnRtcError(string error)
        {
            ErrorReceived?.Invoke(error);
        }

        public void OnRtcClose(string reason)
        {
            Closed?.Invoke(reason);
        }

        public void OnRtcSignal(string json)
        {
            SignalReceived?.Invoke(json);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}
#endif
