using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;

namespace Playserv.Proxy.Implementation
{
#if !UNITY_WEBGL || UNITY_EDITOR
    /// <summary>
    /// Reliable UDP transport for backend endpoints exposed via <c>rudp://host:port</c>.
    /// Uses a simple stop-and-wait protocol with sequence numbers, ACKs and retransmits.
    /// </summary>
    internal sealed class RudpTransportImplementation : DatagramTransportBase
    {
        private const string RudpScheme = "rudp";
        private const byte ProtocolVersion = 1;
        private const int HeaderSize = 6;
        private const int AckTimeoutMs = 350;
        private const int MaxSendAttempts = 8;

        private readonly object _receiveGate = new object();
        private readonly object _sendStateGate = new object();
        private readonly Dictionary<uint, byte[]> _receiveBuffer = new Dictionary<uint, byte[]>();

        private int _nextSendSequence;
        private uint _nextExpectedReceiveSequence = 1;
        private uint _pendingAckSequence;
        private TaskCompletionSource<bool> _pendingAckTcs;

        internal RudpTransportImplementation(string uri, ILogger logger = null)
            : base(uri, RudpScheme, "RUDP", logger)
        {
        }

        public override async Task Send(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var client = GetConnectedClient();
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

                    var acked = await WaitForAckAsync(ackTcs.Task, AckTimeoutMs, DisposeToken);
                    if (acked)
                        return;

                    if (attempt < MaxSendAttempts)
                    {
                        Logger.LogWarning(
                            $"RUDP ACK timeout for seq={sequence}. Retrying {attempt + 1}/{MaxSendAttempts}.");
                    }
                }

                MarkDisconnectedAndCloseClient();
                throw new TimeoutException(
                    $"RUDP ACK was not received for seq={sequence} after {MaxSendAttempts} attempts.");
            }
            catch (OperationCanceledException) when (DisposeToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(RudpTransportImplementation));
            }
            finally
            {
                ClearPendingAck(ackTcs);
            }
        }

        protected override void OnConnected(UdpClient client)
        {
            ResetProtocolState();
        }

        protected override void OnResetConnection()
        {
            ResetProtocolState();
        }

        protected override void OnDispose()
        {
            CancelPendingAck();
        }

        protected override async Task ProcessReceivedDatagramAsync(
            UdpClient client,
            byte[] buffer,
            ObservableByteChannel channel)
        {
            if (buffer == null || buffer.Length < HeaderSize)
            {
                Logger.LogWarning("Received malformed RUDP datagram.");
                return;
            }

            if (!TryParsePacket(buffer, out var packetType, out var sequence, out var payload))
            {
                Logger.LogWarning("Received unsupported RUDP packet.");
                return;
            }

            if (packetType == PacketType.Ack)
            {
                HandleAck(sequence);
                return;
            }

            await SendAckAsync(client, sequence);
            HandleIncomingData(sequence, payload, channel);
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

        private void HandleIncomingData(uint sequence, byte[] payload, ObservableByteChannel channel)
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
                Logger.LogWarning($"Failed to send RUDP ACK for seq={sequence}: {ex.Message}");
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

        private static async Task<bool> WaitForAckAsync(Task<bool> ackTask, int timeoutMs, CancellationToken ct)
        {
            var completed = await AsyncTimeoutHelper.WaitForCompletionOrTimeoutAsync(ackTask, timeoutMs, ct);
            if (!completed)
                return false;

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

        private enum PacketType : byte
        {
            Data = 1,
            Ack = 2
        }
    }
#endif
}
