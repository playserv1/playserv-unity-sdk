using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Matchmaking;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples.NetworkLifecycle
{
    /// <summary>Desktop client. Sign in through the normal SDK before invoking Join from the component menu.</summary>
    public sealed class PlayServTcpRoomClientSample : MonoBehaviour
    {
        [SerializeField] private string functionSlug = "arena";
        [SerializeField] private string roomName = "one";
        private TcpClient _client;
        private CancellationTokenSource _attempt;
        private bool _joining, _pinging;

        private void Awake() => DontDestroyOnLoad(gameObject);

        [ContextMenu("Join room")]
        public async void Join()
        {
            if (_joining || _client != null) return;
            _joining = true;
            using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _attempt = attempt;
            try
            {
                var result = await PlayServMatchmaking.JoinRoomAsync(new PlayServJoinRoomRequest
                { FunctionSlug = functionSlug, RoomName = roomName }, attempt.Token);
                var reservation = result.Reservation;
                if (!result.IsMatched || reservation?.Connect == null || reservation.Connect.Transport != "tcp")
                    throw new IOException("A TCP room endpoint is required.");
                var client = new TcpClient(); _client = client;
                using var cancel = attempt.Token.Register(client.Dispose);
                await client.ConnectAsync(reservation.Connect.Host, reservation.Connect.Port);
                await PlayServTcpRoomTransport.WriteLineAsync(client.GetStream(), reservation.ReservationToken, attempt.Token);
                var ready = await PlayServTcpRoomTransport.ReadLineAsync(client.GetStream(), 128, attempt.Token);
                if (ready == null || !ready.StartsWith("ready plr_", StringComparison.Ordinal)) throw new IOException("Admission refused.");
                Debug.Log("TCP room admission completed.");
            }
            catch { DropConnection(); Debug.LogWarning("TCP room join failed or was cancelled."); }
            finally { _attempt = null; _joining = false; }
        }

        [ContextMenu("Send gameplay ping")]
        public async void Ping()
        {
            if (_client == null || _joining || _pinging) return;
            _pinging = true;
            var client = _client;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var cancel = timeout.Token.Register(client.Dispose);
            try
            {
                await PlayServTcpRoomTransport.WriteLineAsync(client.GetStream(), "ping", timeout.Token);
                var response = await PlayServTcpRoomTransport.ReadLineAsync(client.GetStream(), 128, timeout.Token);
                if (response == null || !response.StartsWith("pong plr_", StringComparison.Ordinal)) throw new IOException();
                Debug.Log("Gameplay ping acknowledged for the admitted player.");
            }
            catch { if (ReferenceEquals(_client, client)) DropConnection(); Debug.LogWarning("TCP game connection ended."); }
            finally { _pinging = false; }
        }

        [ContextMenu("Drop connection (reconnect grace)")]
        public void DropConnection()
        { _attempt?.Cancel(); _client?.Dispose(); _client = null; }

        private void OnDestroy() => DropConnection();
    }
}
