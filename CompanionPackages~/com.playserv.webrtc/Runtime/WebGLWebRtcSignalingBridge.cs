#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using UnityEngine;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebGLWebRtcSignalingBridge : MonoBehaviour
    {
        private static WebGLWebRtcSignalingBridge _instance;

        public static WebGLWebRtcSignalingBridge Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var go = new GameObject("WebGLWebRtcSignalingBridge");
                _instance = go.AddComponent<WebGLWebRtcSignalingBridge>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<string> Closed;

        public string GameObjectName => gameObject.name;

        public void OnRtcSigOpen(string _)
        {
            Opened?.Invoke();
        }

        public void OnRtcSigMessage(string data)
        {
            MessageReceived?.Invoke(data);
        }

        public void OnRtcSigError(string error)
        {
            ErrorReceived?.Invoke(error);
        }

        public void OnRtcSigClose(string reason)
        {
            Closed?.Invoke(reason);
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
