using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.WebRtc
{
    internal sealed class NativeWebRtcSignalingConnection : IWebRtcSignalingConnection
    {
        private readonly Uri _uri;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

        private ClientWebSocket _socket = new ClientWebSocket();
        private Task _receiveLoop;
        private CancellationTokenSource _connectCts;
        private bool _disposed;

        public NativeWebRtcSignalingConnection(Uri uri, ILogger logger)
        {
            _uri = uri ?? throw new ArgumentNullException(nameof(uri));
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
        }

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<Exception> ErrorReceived;
        public event Action<string> Closed;

        public async Task Connect(CancellationToken ct)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeWebRtcSignalingConnection));

            _socket?.Dispose();
            _socket = new ClientWebSocket();
            _connectCts?.Dispose();
            _connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);

            _logger.Log($"Connecting WebRTC signaling websocket: {_uri}");
            await _socket.ConnectAsync(_uri, _connectCts.Token);
            _receiveLoop = Task.Run(ReceiveLoop);
            Opened?.Invoke();
        }

        public async Task SendAsync(string message, CancellationToken ct)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeWebRtcSignalingConnection));

            var bytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
            var segment = new ArraySegment<byte>(bytes);
            await _socket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
        }

        public void Reset()
        {
            if (_disposed)
                return;

            try
            {
                _connectCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _socket?.Abort();
            }
            catch
            {
            }

            try
            {
                _socket?.Dispose();
            }
            catch
            {
            }

            _socket = new ClientWebSocket();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Reset();

            _disposeCts.Cancel();

            // Never block the Unity main thread. `_receiveLoop?.Wait(1000)` was sync-over-async (the
            // loop's continuation needs the main thread, which would be parked in Wait()), a ~1s
            // main-thread freeze on teardown — same bug fixed in WebSocketTransportImplementation.
            // Dispose the socket to abort the pending receive, then drain the loop + dispose the CTSs
            // on a background task.
            try { _socket?.Dispose(); } catch { }
            var loop = _receiveLoop;
            var connectCts = _connectCts;
            var disposeCts = _disposeCts;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try { if (loop != null) await System.Threading.Tasks.Task.WhenAny(loop, System.Threading.Tasks.Task.Delay(1000)).ConfigureAwait(false); }
                catch { }
                connectCts?.Dispose();
                disposeCts.Dispose();
            });
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            using var ms = new MemoryStream();

            try
            {
                while (!_disposeCts.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _socket.ReceiveAsync(segment, _disposeCts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Closed?.Invoke($"{_socket.CloseStatus} {_socket.CloseStatusDescription}".Trim());
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);
                    MessageReceived?.Invoke(json);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(ex);
            }
        }
    }
}
