using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;

namespace Playserv.Samples.NetworkLifecycle
{
    public sealed class PlayServTcpRoomTransport : IPlayServHostedRoomTransport, IPlayServRoomNetworkLifecycle
    {
        private readonly object _sync = new object();
        private readonly HashSet<Connection> _connections = new HashSet<Connection>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private SynchronizationContext _unity;
        private TcpListener _listener;
        private Task _accepting, _stopping;
        public string RoomName { get; private set; }
        public event Action<IPlayServRoomNetworkConnection, string> Authenticating;
        public event Action<IPlayServRoomNetworkConnection> Disconnected;

        public Task StartAsync(PlayServHostedLaunch launch, CancellationToken ct)
        {
#if !UNITY_EDITOR && !UNITY_SERVER
            throw new PlatformNotSupportedException("Run this transport in a Dedicated Server build.");
#else
            ct.ThrowIfCancellationRequested();
            if (_listener != null || _lifetime.IsCancellationRequested) throw new InvalidOperationException("Create a new transport per launch.");
            _unity = SynchronizationContext.Current ?? throw new InvalidOperationException("Start on Unity's main thread.");
            RoomName = launch.RoomName;
            _listener = new TcpListener(IPAddress.Any, launch.ListenPort);
            _listener.Start();
            _accepting = AcceptLoopAsync();
            return Task.CompletedTask;
#endif
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    var socket = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (_lifetime.IsCancellationRequested || _connections.Count >= 64) { socket.Dispose(); continue; }
                        var connection = new Connection(socket);
                        _connections.Add(connection);
                        connection.Running = RunConnectionAsync(connection);
                    }
                }
            }
            catch (Exception) when (_lifetime.IsCancellationRequested) { }
        }

        private async Task RunConnectionAsync(Connection connection)
        {
            try
            {
                var stream = connection.Socket.GetStream();
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    handshake.CancelAfter(TimeSpan.FromSeconds(10));
                    using var cancel = handshake.Token.Register(() => connection.Close("handshake_timeout"));
                    var ticket = await ReadLineAsync(stream, 4096, handshake.Token).ConfigureAwait(false);
                    if (ticket == null) return;
                    PostAuthentication(connection, ticket);
                    ticket = null;
                    await connection.Admitted.Task.ConfigureAwait(false);
                }
                await WriteLineAsync(stream, "ready " + connection.PlayerId, _lifetime.Token).ConfigureAwait(false);
                while (!connection.IsClosed)
                {
                    var message = await ReadLineAsync(stream, 128, _lifetime.Token).ConfigureAwait(false);
                    if (message == null) return;
                    if (message != "ping") throw new IOException("Unsupported sample message.");
                    await WriteLineAsync(stream, "pong " + connection.PlayerId, _lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) { } // Untrusted frames and transport errors close only this connection; never log the ticket.
            finally
            {
                connection.Close("disconnected");
                lock (_sync) _connections.Remove(connection);
                _unity.Post(_ => Disconnected?.Invoke(connection), null);
            }
        }

        private void PostAuthentication(Connection connection, string ticket) => _unity.Post(_ =>
        {
            if (connection.IsClosed) return;
            var authenticate = Authenticating;
            if (authenticate == null) { connection.Close("room_not_ready"); return; }
            try { authenticate(connection, ticket); }
            catch { connection.Close("authentication_failed"); }
        }, null);

        public bool ClosePlayer(string playerId)
        {
            Connection[] connections;
            lock (_sync) connections = _connections.Where(c => c.PlayerId == playerId).ToArray();
            foreach (var connection in connections) connection.Close("player_removed");
            return connections.All(c => c.IsClosed);
        }

        public Task StopAsync(CancellationToken ct)
        {
            Task stopping;
            lock (_sync) stopping = _stopping ?? (_stopping = StopCoreAsync());
            return ct.CanBeCanceled ? WaitForStopAsync(stopping, ct) : stopping;
        }

        private static async Task WaitForStopAsync(Task stopping, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => cancelled.TrySetResult(true));
            await Task.WhenAny(stopping, cancelled.Task);
            ct.ThrowIfCancellationRequested();
            await stopping;
        }

        private async Task StopCoreAsync()
        {
            _lifetime.Cancel(); _listener?.Stop();
            Connection[] connections;
            lock (_sync) connections = _connections.ToArray();
            foreach (var connection in connections) connection.Close("server_stopped");
            if (_accepting != null) await _accepting;
            await Task.WhenAll(connections.Select(c => c.Running));
        }

        internal static async Task<string> ReadLineAsync(Stream stream, int limit, CancellationToken ct)
        {
            var buffer = new byte[limit]; var one = new byte[1];
            for (var length = 0; ; length++)
            {
                if (await stream.ReadAsync(one, 0, 1, ct).ConfigureAwait(false) == 0) return null;
                if (one[0] == (byte)'\n') return new UTF8Encoding(false, true).GetString(buffer, 0, length);
                if (length == limit) throw new IOException("Sample frame exceeds its limit.");
                buffer[length] = one[0];
            }
        }

        internal static Task WriteLineAsync(Stream stream, string message, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            return stream.WriteAsync(bytes, 0, bytes.Length, ct);
        }

        private sealed class Connection : IPlayServRoomNetworkConnection
        {
            private int _closed;
            internal readonly TcpClient Socket;
            internal readonly TaskCompletionSource<bool> Admitted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task Running;
            internal string PlayerId;
            internal bool IsClosed => Volatile.Read(ref _closed) != 0;
            internal Connection(TcpClient socket) { Socket = socket; socket.NoDelay = true; }
            public void Accept(string verifiedPlayerId)
            {
                if (IsClosed) throw new IOException("Connection already closed.");
                PlayerId = verifiedPlayerId;
                Admitted.TrySetResult(true);
            }
            public void Close(string reason)
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                Admitted.TrySetCanceled(); Socket.Dispose();
            }
        }
    }
}
