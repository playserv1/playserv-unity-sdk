#if UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace Playserv.Proxy.Implementation
{
    [Preserve]
    internal sealed class WebGLWebSocketBridge : MonoBehaviour
    {
        private bool _retired;

        public static WebGLWebSocketBridge Create()
        {
            var go = new GameObject("PlayServWebSocket-" + Guid.NewGuid().ToString("N"));
            go.hideFlags = HideFlags.HideAndDontSave;
            var bridge = go.AddComponent<WebGLWebSocketBridge>();
            if (Application.isPlaying)
                DontDestroyOnLoad(go);
            return bridge;
        }

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<string> Closed;

        public string GameObjectName => gameObject.name;

        [Preserve]
        public void OnWsOpen(string _)
        {
            if (!_retired)
                Opened?.Invoke();
        }

        [Preserve]
        public void OnWsMessage(string data)
        {
            if (_retired)
                return;

            string message;
            try
            {
                message = Encoding.UTF8.GetString(Convert.FromBase64String(data ?? string.Empty));
            }
            catch (FormatException)
            {
                ErrorReceived?.Invoke("WebSocket callback could not be decoded.");
                return;
            }

            MessageReceived?.Invoke(message);
        }

        [Preserve]
        public void OnWsError(string error)
        {
            if (!_retired)
                ErrorReceived?.Invoke(error);
        }

        [Preserve]
        public void OnWsClose(string reason)
        {
            if (!_retired)
                Closed?.Invoke(reason);
        }

        public void Retire()
        {
            if (_retired)
                return;

            _retired = true;
            Opened = null;
            MessageReceived = null;
            ErrorReceived = null;
            Closed = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(gameObject);
            else
#endif
                Destroy(gameObject);
        }
    }
}
#endif
