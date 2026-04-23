#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using Playserv.Editor.Proxy;

namespace Playserv.Editor
{
    internal sealed class PlayServConnectionController : IDisposable
    {
        private readonly PlayServWindowState _state;
        private readonly Action _repaint;

        public PlayServConnectionController(PlayServWindowState state, Action repaint)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _repaint = repaint ?? throw new ArgumentNullException(nameof(repaint));
        }

        public async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(_state.WebSocketEndpoint))
            {
                EditorUtility.DisplayDialog("Error", "Endpoint cannot be empty", "OK");
                return;
            }

            try
            {
                DisposeTransport();
                _state.WebSocketTransport = new EditorWebSocketTransport(_state.WebSocketEndpoint);
                _state.WebSocketTransport.OnConnected += HandleTransportChanged;
                _state.WebSocketTransport.OnMessageReceived += HandleTransportMessage;
                _state.WebSocketTransport.OnError += HandleTransportError;
                _state.WebSocketTransport.OnDisconnected += HandleTransportDisconnected;

                _repaint();
                await _state.WebSocketTransport.ConnectAsync();
                _repaint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Connection Error", ex.Message, "OK");
            }
        }

        public void Disconnect()
        {
            _state.WebSocketTransport?.Disconnect();
            _repaint();
        }

        public async Task SendTestMessageAsync()
        {
            if (_state.WebSocketTransport == null || !_state.WebSocketTransport.IsConnected)
                return;

            await _state.WebSocketTransport.SendAsync(_state.TestMessage);
            _repaint();
        }

        public void ClearLogs()
        {
            _state.WebSocketTransport?.ClearLogs();
            _repaint();
        }

        public void Dispose()
        {
            DisposeTransport();
        }

        private void HandleTransportChanged()
        {
            _repaint();
        }

        private void HandleTransportMessage(string _)
        {
            _repaint();
        }

        private void HandleTransportError(string _)
        {
            _repaint();
        }

        private void HandleTransportDisconnected()
        {
            _repaint();
        }

        private void DisposeTransport()
        {
            if (_state.WebSocketTransport == null)
                return;

            _state.WebSocketTransport.OnConnected -= HandleTransportChanged;
            _state.WebSocketTransport.OnMessageReceived -= HandleTransportMessage;
            _state.WebSocketTransport.OnError -= HandleTransportError;
            _state.WebSocketTransport.OnDisconnected -= HandleTransportDisconnected;
            _state.WebSocketTransport.Dispose();
            _state.WebSocketTransport = null;
        }
    }
}
#endif
