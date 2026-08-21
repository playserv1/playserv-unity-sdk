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
