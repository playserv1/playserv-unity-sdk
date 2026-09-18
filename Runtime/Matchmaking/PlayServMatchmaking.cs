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
        /// Creates an independent, single-attempt game WebSocket. Subscribe before ConnectAsync.
        /// Options are copied now; the existing player session is resolved only when connecting.
        /// Success means handshake sent, never confirmed admission. Does not touch platform /ws.
        /// </summary>
        public static PlayServGameConnection CreateGameConnection(PlayServGameConnectionOptions options = null) =>
            new PlayServGameConnection(options, () => PlayServ.Settings,
                Playserv.Proxy.Common.TransportImplementationResolver.Create,
                new Playserv.Serialization.NewtonsoftJsonCodec(), SynchronizationContext.Current);

        /// <summary>
        /// Requests a platform-named room and its hosting player's reservation using the signed-in player.
        /// Attributes are wishes; the returned server-approved attributes are authoritative.
        /// Performs one bounded request with no automatic retry, launch, additional join or transport connection.
        /// Call on the Unity context to resume there. Cancellation after sending cannot undo room creation.
        /// </summary>
        public static Task<PlayServMatchResult> HostRoomAsync(PlayServHostRoomRequest request,
            PlayServRoomHostOptions options = null, CancellationToken ct = default) =>
            PlayServMatchmakingClient.CreateDefault().HostRoomAsync(request, options, ct);

        /// <summary>
        /// Reads the public room browser with a player session. Requires the /rooms API (PSV-2600).
        /// The caller chooses a room and explicitly requests any subsequent cursor page.
        /// </summary>
        public static Task<PlayServRoomBrowsePage> BrowseRoomsAsync(
            string functionSlug, PlayServRoomBrowseQuery query = null, CancellationToken ct = default) =>
            PlayServMatchmakingClient.CreateDefault().BrowseRoomsAsync(functionSlug, query, ct);

        /// <summary>
        /// Requests one reservation for the named room. Requires the /rooms API (PSV-2600).
        /// Does not place, launch, poll, retry or connect a game transport.
        /// </summary>
        public static Task<PlayServMatchResult> JoinRoomAsync(
            string functionSlug, string roomName, CancellationToken ct = default) =>
            PlayServMatchmakingClient.CreateDefault().JoinRoomAsync(functionSlug, roomName, ct);

        /// <summary>Requests one named-room reservation with snapshotted admission parameters. No automatic retries.</summary>
        public static Task<PlayServMatchResult> JoinRoomAsync(PlayServJoinRoomRequest request, CancellationToken ct = default) =>
            PlayServMatchmakingClient.CreateDefault().JoinRoomAsync(request, ct);

        /// <summary>Opt-in bounded named join. When provided, connector must be supplied from Unity's synchronization context.</summary>
        public static Task<PlayServMatchReservation> JoinRoomAndConnectAsync(PlayServJoinRoomRequest request,
            PlayServRoomJoinOptions options = null, Func<PlayServMatchReservation, CancellationToken, Task> connector = null,
            CancellationToken ct = default) =>
            PlayServMatchmakingClient.CreateDefault().JoinRoomAndConnectAsync(request, options, connector, ct);

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
