using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
    public sealed class WebSocketTransportImplementation : ITransportImplementation
    {
        private readonly Uri _uri;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private ObservableByteChannel _channel;
        private readonly SynchronizationContext _syncContext;
        private readonly ILogger _logger;
        private readonly object _connectGate = new object();
        
        private bool _connecting;
        private ClientWebSocket _socket = new ClientWebSocket();
        private CancellationTokenSource _connectCTS;
        private Task _receiveLoop;

        public WebSocketTransportImplementation(string uri, ILogger logger = null)
        {
            _uri = new Uri(uri);
            _logger = logger ?? PlayServLog.ForCategory(PlayServLogCategory.Transport);
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ObservableByteChannel(_observers, _gate, _syncContext);
        }

        public async Task<bool> Connect()
        {
            lock (_connectGate)
            {
                if (_connecting)
                {
                    _logger.LogWarning("WebSocket connect already in progress.");
                    return false;
                }

                if (_socket != null && _socket.State == WebSocketState.Open)
                {
                    if (_channel.IsCompleted)
                    {
                        _logger.LogWarning("WebSocket is open but receive channel is completed. Recreating socket/channel.");
                        try
                        {
                            _socket.Abort();
                        }
                        catch
                        {
                            // ignored
                        }

                        _socket.Dispose();
                        _socket = new ClientWebSocket();
                        ResetChannel();
                    }
                    else
                    {
                        _logger.LogWarning("WebSocket is already connected.");
                        return true;
                    }
                }
                else
                {
                    _socket?.Dispose();
                    _socket = new ClientWebSocket();
                    // Keep the channel instance on fresh connect to preserve existing transport subscription.
                    // Recreate only after previous channel was completed by disconnect/error.
                    if (_channel.IsCompleted)
                        ResetChannel();
                }

                _connectCTS?.Dispose();
                _connectCTS = new CancellationTokenSource();

                _connecting = true;
            }

            try
            {
                _logger.Log($"Connecting to WebSocket: {_uri}");
                await _socket.ConnectAsync(_uri, _connectCTS.Token);

                _receiveLoop = Task.Run(ReceiveLoop);

                _logger.Log("WebSocket connected successfully.");
                return true;
            }
            catch (OperationCanceledException)
            {
                try { _socket.Abort(); }
                catch
                {
                    // ignored
                }

                _logger.LogWarning("WebSocket connect cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to connect to WebSocket: {ex.Message}");
                return false;
            }
            finally
            {
                lock (_connectGate) { _connecting = false; }
            }
        }

        public async Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (_socket.State != WebSocketState.Open)
            {
                _logger.LogError("Attempted to send data but WebSocket is not connected.");
                throw new InvalidOperationException("WebSocket is not connected.");
            }

            var isRespawnFrame = OutboundPacketDiagnostics.BeginSocketWrite(data, _logger);
            try
            {
                var segment = new ArraySegment<byte>(data);
                await _socket.SendAsync(
                    segment,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: CancellationToken.None);
                OutboundPacketDiagnostics.CompleteSocketWrite(isRespawnFrame, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send data via WebSocket: {ex.Message}");
                throw;
            }
        }

        public IObservable<byte[]> OnReceive() => _channel;

        public void ResetConnection()
        {
            lock (_connectGate)
            {
                _connecting = false;

                try
                {
                    _connectCTS?.Cancel();
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
                ResetChannel();
                _logger.Log("[WebSocket] Transport connection state reset.");
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            using var ms = new MemoryStream();
            ObservableByteChannel channel;

            lock (_gate)
            {
                channel = _channel;
            }

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _socket.ReceiveAsync(segment, _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        var closeStatus = result.CloseStatus?.ToString() ?? "n/a";
                        var closeDescription = string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                            ? "n/a"
                            : result.CloseStatusDescription;
                        _logger.LogWarning(
                            $"WebSocket close frame received. status={closeStatus}, description={closeDescription}, socketState={_socket.State}");

                        try
                        {
                            if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                            {
                                await _socket.CloseAsync(
                                    result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                                    result.CloseStatusDescription,
                                    CancellationToken.None);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to complete websocket close handshake: {ex.Message}");
                        }

                        channel.Complete();
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (!result.EndOfMessage)
                        continue;

                    var data = ms.ToArray();
                    ms.SetLength(0);

                    channel.Next(data);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Log("WebSocket receive loop cancelled.");
                channel.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in WebSocket receive loop: {ex.Message}");
                channel.Error(ex);
            }
        }

        private void ResetChannel()
        {
            lock (_gate)
            {
                _observers.Clear();
                _channel = new ObservableByteChannel(_observers, _gate, _syncContext);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();

            // Never block the Unity main thread here. The old `_receiveLoop?.Wait(1000)` was
            // sync-over-async: the receive loop's continuations marshal back to the main thread via
            // the captured SynchronizationContext, but that thread is parked inside Wait() — so the
            // continuation can't run and Wait() burns the full 1000ms every teardown (a hard ~1s
            // main-thread freeze). Disposing the socket aborts the pending ReceiveAsync so the loop
            // unwinds on its own; the loop's resources are then cleaned up on a background task.
            try { _socket.Dispose(); } catch { }

            var loop = _receiveLoop;
            var connectCts = _connectCTS;
            var cts = _cts;
            _ = Task.Run(async () =>
            {
                try { if (loop != null) await Task.WhenAny(loop, Task.Delay(1000)).ConfigureAwait(false); }
                catch { }
                connectCts?.Dispose();
                cts.Dispose();
            });
        }
    }
}
