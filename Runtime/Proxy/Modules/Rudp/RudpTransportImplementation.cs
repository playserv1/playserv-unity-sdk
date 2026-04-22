using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
#if !UNITY_WEBGL || UNITY_EDITOR
    /// <summary>
    /// Reliable UDP transport for backend endpoints exposed via <c>rudp://host:port</c>.
    /// Uses a simple stop-and-wait protocol with sequence numbers, ACKs and retransmits.
    /// </summary>
    public sealed class RudpTransportImplementation : ITransportImplementation
    {
        private const string RudpScheme = "rudp";
        private const byte ProtocolVersion = 1;
        private const int HeaderSize = 6;
        private const int AckTimeoutMs = 350;
        private const int MaxSendAttempts = 8;

        private readonly Uri _uri;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private readonly object _connectGate = new object();
        private readonly object _receiveGate = new object();
        private readonly object _sendStateGate = new object();
        private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
        private readonly Dictionary<uint, byte[]> _receiveBuffer = new Dictionary<uint, byte[]>();
        private readonly SynchronizationContext _syncContext;
        private ByteArrayChannel _channel;

        private UdpClient _client;
        private Task _receiveLoop;
        private bool _connected;
        private bool _connecting;
        private int _nextSendSequence;
        private uint _nextExpectedReceiveSequence = 1;
        private uint _pendingAckSequence;
        private TaskCompletionSource<bool> _pendingAckTcs;

        public RudpTransportImplementation(string uri, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new ArgumentException("RUDP endpoint cannot be null or empty.", nameof(uri));

            _uri = new Uri(uri);
            if (!string.Equals(_uri.Scheme, RudpScheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"RUDP transport requires '{RudpScheme}://' endpoint. Actual scheme: '{_uri.Scheme}'.",
                    nameof(uri));
            }

            if (_uri.Port <= 0)
                throw new ArgumentException("RUDP endpoint must include an explicit port.", nameof(uri));

            _logger = logger ?? new ConsoleLogger();
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _channel = new ByteArrayChannel(_observers, _gate, _syncContext);
        }

        public Task<bool> Connect()
        {
            lock (_connectGate)
            {
                if (_connecting)
                {
                    _logger.LogWarning("RUDP connect already in progress.");
                    return Task.FromResult(false);
                }

                if (_connected)
                {
                    if (_channel.IsCompleted)
                    {
                        _logger.LogWarning("RUDP transport is marked connected but receive channel is completed. Recreating client/channel.");
                        CloseClientUnsafe();
                        _connected = false;
                        ResetChannel();
                    }
                    else
                    {
                        _logger.LogWarning("RUDP transport is already connected.");
                        return Task.FromResult(true);
                    }
                }
                else
                {
                    CloseClientUnsafe();
                    if (_channel.IsCompleted)
                        ResetChannel();
                }

                _connecting = true;
            }

            try
            {
                var client = new UdpClient();
                client.Connect(_uri.Host, _uri.Port);

                lock (_connectGate)
                {
                    _client = client;
                    _connected = true;
                }

                ResetProtocolState();
                _receiveLoop = Task.Run(() => ReceiveLoop(client));
                _logger.Log($"RUDP transport ready: {_uri}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize RUDP transport: {ex.Message}");
                CloseClientUnsafe();
                _connected = false;
                return Task.FromResult(false);
            }
            finally
            {
                lock (_connectGate)
                {
                    _connecting = false;
                }
            }
        }

        public async Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            UdpClient client;
            lock (_connectGate)
            {
                client = _client;
                if (!_connected || client == null)
                {
                    _logger.LogError("Attempted to send data but RUDP transport is not connected.");
                    throw new InvalidOperationException("RUDP transport is not connected.");
                }
            }

            var sequence = unchecked((uint)Interlocked.Increment(ref _nextSendSequence));
            var packet = BuildPacket(PacketType.Data, sequence, data);
            var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sendStateGate)
            {
                if (_pendingAckTcs != null)
                {
                    throw new InvalidOperationException(
                        "RUDP transport does not support overlapping sends. Await the current send before sending the next payload.");
                }

                _pendingAckSequence = sequence;
                _pendingAckTcs = ackTcs;
            }

            try
            {
                for (var attempt = 1; attempt <= MaxSendAttempts; attempt++)
                {
                    await client.SendAsync(packet, packet.Length);

                    var acked = await WaitForAckAsync(ackTcs.Task, AckTimeoutMs, _disposeCts.Token);
                    if (acked)
                        return;

                    if (attempt < MaxSendAttempts)
                    {
                        _logger.LogWarning(
                            $"RUDP ACK timeout for seq={sequence}. Retrying {attempt + 1}/{MaxSendAttempts}.");
                    }
                }

                MarkDisconnectedAfterSendFailure();
                throw new TimeoutException(
                    $"RUDP ACK was not received for seq={sequence} after {MaxSendAttempts} attempts.");
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(RudpTransportImplementation));
            }
            finally
            {
                ClearPendingAck(ackTcs);
            }
        }

        public IObservable<byte[]> OnReceive() => _channel;

        public void ResetConnection()
        {
            lock (_connectGate)
            {
                _connecting = false;
                _connected = false;
                CloseClientUnsafe();
                ResetProtocolState();
                ResetChannel();
            }

            _logger.Log("[RUDP] Transport connection state reset.");
        }

        public void Dispose()
        {
            _disposeCts.Cancel();
            CloseClientUnsafe();
            CancelPendingAck();

            try
            {
                _receiveLoop?.Wait(1000);
            }
            catch
            {
                // ignored
            }

            _disposeCts.Dispose();
        }

        private async Task ReceiveLoop(UdpClient client)
        {
            ByteArrayChannel channel;
            lock (_gate)
            {
                channel = _channel;
            }

            try
            {
                while (!_disposeCts.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync();
                    if (result.Buffer == null || result.Buffer.Length < HeaderSize)
                    {
                        _logger.LogWarning("Received malformed RUDP datagram.");
                        continue;
                    }

                    if (!TryParsePacket(result.Buffer, out var packetType, out var sequence, out var payload))
                    {
                        _logger.LogWarning("Received unsupported RUDP packet.");
                        continue;
                    }

                    if (packetType == PacketType.Ack)
                    {
                        HandleAck(sequence);
                        continue;
                    }

                    await SendAckAsync(client, sequence);
                    HandleIncomingData(sequence, payload, channel);
                }
            }
            catch (ObjectDisposedException)
            {
                if (_disposeCts.IsCancellationRequested)
                    channel.Complete();
            }
            catch (SocketException ex)
            {
                if (_disposeCts.IsCancellationRequested)
                {
                    channel.Complete();
                    return;
                }

                _logger.LogError($"Error in RUDP receive loop: {ex.Message}");
                channel.Error(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in RUDP receive loop: {ex.Message}");
                channel.Error(ex);
            }
        }

        private void HandleAck(uint sequence)
        {
            TaskCompletionSource<bool> ackTcs = null;

            lock (_sendStateGate)
            {
                if (_pendingAckTcs != null && _pendingAckSequence == sequence)
                    ackTcs = _pendingAckTcs;
            }

            ackTcs?.TrySetResult(true);
        }

        private void HandleIncomingData(uint sequence, byte[] payload, ByteArrayChannel channel)
        {
            lock (_receiveGate)
            {
                if (sequence < _nextExpectedReceiveSequence)
                    return;

                if (sequence > _nextExpectedReceiveSequence)
                {
                    if (!_receiveBuffer.ContainsKey(sequence))
                        _receiveBuffer[sequence] = payload;

                    return;
                }

                channel.Next(payload);
                _nextExpectedReceiveSequence++;

                while (_receiveBuffer.TryGetValue(_nextExpectedReceiveSequence, out var bufferedPayload))
                {
                    _receiveBuffer.Remove(_nextExpectedReceiveSequence);
                    channel.Next(bufferedPayload);
                    _nextExpectedReceiveSequence++;
                }
            }
        }

        private async Task SendAckAsync(UdpClient client, uint sequence)
        {
            try
            {
                var ackPacket = BuildPacket(PacketType.Ack, sequence, null);
                await client.SendAsync(ackPacket, ackPacket.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to send RUDP ACK for seq={sequence}: {ex.Message}");
            }
        }

        private void ResetProtocolState()
        {
            lock (_receiveGate)
            {
                _receiveBuffer.Clear();
                _nextExpectedReceiveSequence = 1;
            }

            Interlocked.Exchange(ref _nextSendSequence, 0);
            CancelPendingAck();
        }

        private void CancelPendingAck()
        {
            TaskCompletionSource<bool> ackTcs = null;

            lock (_sendStateGate)
            {
                ackTcs = _pendingAckTcs;
                _pendingAckTcs = null;
                _pendingAckSequence = 0;
            }

            ackTcs?.TrySetCanceled();
        }

        private void ClearPendingAck(TaskCompletionSource<bool> ackTcs)
        {
            lock (_sendStateGate)
            {
                if (!ReferenceEquals(_pendingAckTcs, ackTcs))
                    return;

                _pendingAckTcs = null;
                _pendingAckSequence = 0;
            }
        }

        private void MarkDisconnectedAfterSendFailure()
        {
            lock (_connectGate)
            {
                _connected = false;
                CloseClientUnsafe();
            }
        }

        private static async Task<bool> WaitForAckAsync(Task<bool> ackTask, int timeoutMs, CancellationToken ct)
        {
            var completed = await Task.WhenAny(ackTask, Task.Delay(timeoutMs, ct));
            if (completed != ackTask)
            {
                ct.ThrowIfCancellationRequested();
                return false;
            }

            return await ackTask;
        }

        private static byte[] BuildPacket(PacketType packetType, uint sequence, byte[] payload)
        {
            var payloadLength = payload?.Length ?? 0;
            var packet = new byte[HeaderSize + payloadLength];
            packet[0] = ProtocolVersion;
            packet[1] = (byte)packetType;
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(2, 4), sequence);

            if (payloadLength > 0)
                Buffer.BlockCopy(payload, 0, packet, HeaderSize, payloadLength);

            return packet;
        }

        private static bool TryParsePacket(byte[] buffer, out PacketType packetType, out uint sequence, out byte[] payload)
        {
            packetType = PacketType.Data;
            sequence = 0;
            payload = null;

            if (buffer == null || buffer.Length < HeaderSize || buffer[0] != ProtocolVersion)
                return false;

            if (buffer[1] != (byte)PacketType.Data && buffer[1] != (byte)PacketType.Ack)
                return false;

            packetType = (PacketType)buffer[1];
            sequence = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(2, 4));
            var payloadLength = buffer.Length - HeaderSize;
            payload = payloadLength <= 0 ? Array.Empty<byte>() : CopyPayload(buffer, payloadLength);
            return true;
        }

        private static byte[] CopyPayload(byte[] buffer, int payloadLength)
        {
            var payload = new byte[payloadLength];
            Buffer.BlockCopy(buffer, HeaderSize, payload, 0, payloadLength);
            return payload;
        }

        private void ResetChannel()
        {
            lock (_gate)
            {
                _observers.Clear();
                _channel = new ByteArrayChannel(_observers, _gate, _syncContext);
            }
        }

        private void CloseClientUnsafe()
        {
            try
            {
                _client?.Close();
            }
            catch
            {
                // ignored
            }

            try
            {
                _client?.Dispose();
            }
            catch
            {
                // ignored
            }

            _client = null;
        }

        private enum PacketType : byte
        {
            Data = 1,
            Ack = 2
        }

        private sealed class ByteArrayChannel : IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers;
            private readonly object _gate;
            private readonly SynchronizationContext _syncContext;
            private bool _completed;

            public ByteArrayChannel(List<IObserver<byte[]>> observers, object gate, SynchronizationContext syncContext)
            {
                _observers = observers;
                _gate = gate;
                _syncContext = syncContext ?? new SynchronizationContext();
            }

            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                if (observer == null)
                    throw new ArgumentNullException(nameof(observer));

                lock (_gate)
                {
                    if (_completed)
                    {
                        observer.OnCompleted();
                        return new Unsubscriber(_observers, observer, _gate, false);
                    }

                    _observers.Add(observer);
                    return new Unsubscriber(_observers, observer, _gate, true);
                }
            }

            public void Next(byte[] value)
            {
                IObserver<byte[]>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    snapshot = _observers.ToArray();
                }

                foreach (var observer in snapshot)
                    _syncContext.Post(_ => observer.OnNext(value), null);
            }

            public void Error(Exception error)
            {
                IObserver<byte[]>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    _completed = true;
                    snapshot = _observers.ToArray();
                    _observers.Clear();
                }

                foreach (var observer in snapshot)
                    _syncContext.Post(_ => observer.OnError(error), null);
            }

            public void Complete()
            {
                IObserver<byte[]>[] snapshot;

                lock (_gate)
                {
                    if (_completed)
                        return;

                    _completed = true;
                    snapshot = _observers.ToArray();
                    _observers.Clear();
                }

                foreach (var observer in snapshot)
                    _syncContext.Post(_ => observer.OnCompleted(), null);
            }

            public bool IsCompleted
            {
                get
                {
                    lock (_gate)
                    {
                        return _completed;
                    }
                }
            }

            private sealed class Unsubscriber : IDisposable
            {
                private readonly List<IObserver<byte[]>> _observers;
                private readonly IObserver<byte[]> _observer;
                private readonly object _gate;
                private bool _active;

                public Unsubscriber(List<IObserver<byte[]>> observers, IObserver<byte[]> observer, object gate, bool active)
                {
                    _observers = observers;
                    _observer = observer;
                    _gate = gate;
                    _active = active;
                }

                public void Dispose()
                {
                    if (!_active)
                        return;

                    _active = false;

                    lock (_gate)
                    {
                        _observers.Remove(_observer);
                    }
                }
            }
        }
    }
#endif
}
