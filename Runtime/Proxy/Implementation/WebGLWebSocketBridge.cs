#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using UnityEngine;

namespace Playserv.Proxy.Implementation
{
    internal sealed class WebGLWebSocketBridge : MonoBehaviour
    {
        private static WebGLWebSocketBridge _instance;

        public static WebGLWebSocketBridge Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var go = new GameObject("WebGLWebSocketBridge");
                _instance = go.AddComponent<WebGLWebSocketBridge>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<string> Closed;

        public string GameObjectName => gameObject.name;

        public void OnWsOpen(string _)
        {
            Opened?.Invoke();
        }

        public void OnWsMessage(string data)
        {
            MessageReceived?.Invoke(data);
        }

        public void OnWsError(string error)
        {
            ErrorReceived?.Invoke(error);
        }

        public void OnWsClose(string reason)
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
