using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Playserv.Editor.Proxy
{
    public sealed class EditorWebSocketTransport : IDisposable
    {
        private readonly Uri _uri;
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private bool _isConnected;
        private bool _isConnecting;
        
        private readonly List<string> _receivedMessages = new List<string>();
        private readonly List<string> _logMessages = new List<string>();

        public bool IsConnected => _isConnected;
        public bool IsConnecting => _isConnecting;
        public IReadOnlyList<string> ReceivedMessages => _receivedMessages;
        public IReadOnlyList<string> LogMessages => _logMessages;

        public event Action OnConnected;
        public event Action<string> OnMessageReceived;
        public event Action<string> OnError;
        public event Action OnDisconnected;

        public EditorWebSocketTransport(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new ArgumentException("URI cannot be null or empty", nameof(uri));

            _uri = new Uri(uri);
        }

        public async Task<bool> ConnectAsync()
        {
            if (_isConnected)
            {
                LogMessage("Already connected");
                return true;
            }

            if (_isConnecting)
            {
                LogMessage("Connection already in progress");
                return false;
            }

            _isConnecting = true;

            try
            {
                _socket?.Dispose();
                _socket = new ClientWebSocket();
                
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                LogMessage($"Connecting to {_uri}...");
                
                await _socket.ConnectAsync(_uri, _cts.Token);

                _isConnected = true;
                _isConnecting = false;

                LogMessage("Connected successfully");
                OnConnected?.Invoke();

                _receiveTask = Task.Run(ReceiveLoop);

                return true;
            }
            catch (Exception ex)
            {
                _isConnecting = false;
                _isConnected = false;
                var errorMsg = $"Connection failed: {ex.Message}";
                LogMessage(errorMsg);
                OnError?.Invoke(errorMsg);
                return false;
            }
        }

        public async Task<bool> SendAsync(string message)
        {
            if (!_isConnected || _socket?.State != WebSocketState.Open)
            {
                LogMessage("Cannot send: not connected");
                return false;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                var segment = new ArraySegment<byte>(bytes);
                
                await _socket.SendAsync(segment, WebSocketMessageType.Text, true, _cts.Token);
                
                LogMessage($"Sent: {message}");
                return true;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Send failed: {ex.Message}";
                LogMessage(errorMsg);
                OnError?.Invoke(errorMsg);
                return false;
            }
        }

        public void Disconnect()
        {
            if (!_isConnected && !_isConnecting)
                return;

            _cts?.Cancel();

            try
            {
                if (_socket?.State == WebSocketState.Open)
                {
                    _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None)
                        .Wait(1000);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            _isConnected = false;
            _isConnecting = false;

            LogMessage("Disconnected");
            OnDisconnected?.Invoke();
        }

        public void ClearLogs()
        {
            _logMessages.Clear();
            _receivedMessages.Clear();
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            using var ms = new MemoryStream();

            try
            {
                while (_isConnected && !_cts.Token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _socket.ReceiveAsync(segment, _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _isConnected = false;
                        LogMessage("Server closed connection");
                        EditorApplication.delayCall += () => OnDisconnected?.Invoke();
                        break;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (!result.EndOfMessage)
                        continue;

                    var message = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);

                    _receivedMessages.Add(message);
                    LogMessage($"Received: {message}");
                    
                    EditorApplication.delayCall += () => OnMessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("Receive loop cancelled");
            }
            catch (Exception ex)
            {
                var errorMsg = $"Receive error: {ex.Message}";
                LogMessage(errorMsg);
                EditorApplication.delayCall += () => OnError?.Invoke(errorMsg);
            }
            finally
            {
                _isConnected = false;
            }
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logMessages.Add($"[{timestamp}] {message}");
        }

        public void Dispose()
        {
            Disconnect();
            _socket?.Dispose();
            _cts?.Dispose();
        }
    }
}
