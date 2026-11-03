using System.Threading;
using System.Threading.Tasks;
using Playserv.Matchmaking;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>Player-safe matchmaking with typed lobby state and server launch.</summary>
    public sealed class PlayServMatchmakingSample : MonoBehaviour
    {
        private PlayServGameConnection _gameConnection;

        /// <summary>Opens the optional C# game WebSocket with Host's existing ticket, without another Join.</summary>
        public async Task HostAndOpenGameSocketAsync(PlayServHostRoomRequest request, CancellationToken ct)
        {
            var connection = PrepareGameConnection();
            try
            {
                var hosted = await PlayServMatchmaking.HostRoomAsync(request, ct: ct);
                await connection.ConnectAsync(hosted.Reservation, ct);
            }
            catch { connection.Dispose(); throw; }
            // HandshakeSent is not admission. Wait for a message defined by your game's server protocol.
        }

        /// <summary>Uses bounded named join and the same opt-in connector; the game still decides readiness.</summary>
        public async Task JoinAndOpenGameSocketAsync(string slug, string roomName, CancellationToken ct)
        {
            var connection = PrepareGameConnection();
            try
            {
                await PlayServMatchmaking.JoinRoomAndConnectAsync(new PlayServJoinRoomRequest
                    { FunctionSlug = slug, RoomName = roomName }, connector: connection.ConnectAsync, ct: ct);
            }
            catch { connection.Dispose(); throw; }
        }

        private PlayServGameConnection PrepareGameConnection()
        {
            _gameConnection?.Dispose();
            _gameConnection = PlayServMatchmaking.CreateGameConnection();
            _gameConnection.MessageReceived += OnGameMessage;
            _gameConnection.Closed += error =>
            {
                if (error != null) Debug.LogWarning(error.Message);
            };
            return _gameConnection;
        }

        private void OnGameMessage(string text)
        {
            // Parse your game's protocol here. Do not treat an arbitrary welcome/echo as a universal admission ack.
            // Avoid logging messages: they can contain player data or credentials owned by the game.
        }

        private void OnDestroy() => _gameConnection?.Dispose();

        /// <summary>Returns the host's existing reservation; the game owns travel, without another Join.</summary>
        public async Task<PlayServMatchReservation> HostFriendsAsync(string title, int maxPlayers,
            int bots, CancellationToken cancellationToken)
        {
            var result = await PlayServMatchmaking.HostRoomAsync(new PlayServHostRoomRequest
            {
                FunctionSlug = "tank-room",
                Attributes = new { title, max_players = maxPlayers, bots }
            }, new PlayServRoomHostOptions { Timeout = System.TimeSpan.FromSeconds(45) }, cancellationToken);
            // Share RoomName as the invite code; display result.Reservation.Attributes, not the wishes above.
            // Connect may be null. Pass a usable endpoint and the secret token to game-owned networking.
            // Do not log the token or automatically repeat Host after an ambiguous failure.
            return result.Reservation;
        }

        public async Task<PlayServJoinGameResult> JoinRankedAsync(
            string partyId,
            CancellationToken cancellationToken)
        {
            return await PlayServMatchmaking.JoinGameAsync(
                new PlayServJoinGameRequest
                {
                    FunctionSlug = "tank-room",
                    Matchmaker = "ranked",
                    Parameters = new
                    {
                        mode = "duo",
                        party_id = partyId
                    }
                },
                cancellationToken: cancellationToken);
        }

        public async Task<PlayServServerLaunchResult> LaunchEuropeServerAsync(
            CancellationToken cancellationToken)
        {
            PlayServServerLaunchResult launch =
                await PlayServMatchmaking.LaunchServerAsync(
                    "tank-room",
                    "eu-west",
                    cancellationToken);

            Debug.Log($"PlayServ accepted deployment {launch.DeploymentId}; waiting for room registration.");
            return launch;
        }
    }
}
