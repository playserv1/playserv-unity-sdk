using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Matchmaking;

namespace Playserv.Wrapper
{
    /// <summary>Player-facing matchmaking operations over the runtime REST API.</summary>
    public static class PlayServMatchmaking
    {
        /// <summary>
        /// Executes one placement request. The HTTP deadline is derived from
        /// <paramref name="waitMs"/> and includes a five-second network margin.
        /// </summary>
        public static Task<PlayServMatchResult> FindMatchAsync(
            string functionSlug,
            string matchmaker = null,
            int waitMs = 0,
            int searchAgeMs = 0,
            CancellationToken cancellationToken = default) =>
            PlayServMatchmakingClient.CreateDefault().FindMatchAsync(
                functionSlug,
                matchmaker,
                waitMs,
                searchAgeMs,
                cancellationToken);

        /// <summary>
        /// Executes one placement request, including an optional typed lobby-state
        /// object forwarded through the backend's <c>params</c> field.
        /// </summary>
        public static Task<PlayServMatchResult> FindMatchAsync(
            PlayServFindMatchRequest request,
            CancellationToken cancellationToken = default) =>
            PlayServMatchmakingClient.CreateDefault().FindMatchAsync(
                request,
                cancellationToken);

        /// <summary>
        /// Waits for placement using 20-second server long polls. Each request
        /// gets a 25-second HTTP deadline derived from that same wait value.
        /// </summary>
        public static Task<PlayServJoinGameResult> JoinGameAsync(
            string functionSlug,
            string matchmaker = null,
            Func<PlayServMatchReservation, CancellationToken, Task> tryEnter = null,
            CancellationToken cancellationToken = default) =>
            PlayServMatchmakingClient.CreateDefault().JoinGameAsync(
                functionSlug,
                matchmaker,
                tryEnter,
                cancellationToken);

        /// <summary>
        /// Waits for placement with a stable lobby-state snapshot and a configurable
        /// per-poll server wait. Search age is maintained by the SDK.
        /// </summary>
        public static Task<PlayServJoinGameResult> JoinGameAsync(
            PlayServJoinGameRequest request,
            Func<PlayServMatchReservation, CancellationToken, Task> tryEnter = null,
            CancellationToken cancellationToken = default) =>
            PlayServMatchmakingClient.CreateDefault().JoinGameAsync(
                request,
                tryEnter,
                cancellationToken);

        /// <summary>
        /// Requests a game-server launch through PlayServ. A successful response
        /// means the deployment was accepted; the room appears after self-registration.
        /// </summary>
        public static Task<PlayServServerLaunchResult> LaunchServerAsync(
            string functionSlug,
            string region = null,
            CancellationToken cancellationToken = default) =>
            PlayServMatchmakingClient.CreateDefault().LaunchServerAsync(
                functionSlug,
                region,
                cancellationToken);
    }
}
