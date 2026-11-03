using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;
using Playserv.Serialization;
using UnityEngine;

public sealed class PlayServDedicatedServerSample : MonoBehaviour
{
    // Invoke explicitly after Configure in a development build; never an initialization gate.
    public async Task CheckDevelopmentSchema(Type[] types, CancellationToken ct)
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        foreach (var check in await PlayServGameServer.CheckSchemaAsync(types, ct))
            Debug.Log($"Schema advisory: {check.EntityName}: {check.Availability}");
#else
        await Task.CompletedTask;
#endif
    }

    [SerializeField] private string functionSlug = "ranked-arena";
    [SerializeField] private string roomName = "unity-room-1";
    [SerializeField] private int capacity = 16;

    private PlayServGameRoomHandle _room;
    private PlayServUplinkDataSubscription<UplinkProgress> _progress;
    // Optional metadata for your room-preparation code; this performs no I/O or registration.
    public PlayServGameRoomConnect ReadLaunchEndpoint(int listenPort, PlayServGameRoomConnect explicitConnect = null) =>
        PlayServServerLaunch.ResolveConnect(listenPort, explicitConnect);
    // Optional connection tracking over the same required pushed-ticket path: choose one owner per player.
    public PlayServRoomConnectionTracker TrackConnections(Action<PlayServConnectionRemoval> disconnectExactConnection)
    {
        var tracker = _room.CreateConnectionTracker();
        tracker.ConnectionRemoved += disconnectExactConnection;
        return tracker;
    }
    // Reserve a specific room for a player; this does not connect the player's game transport.
    public Task<PlayServServerMatchResult> ReserveNamedRoom(string playerId, string targetRoom, CancellationToken ct) =>
        PlayServGameServer.JoinRoomForPlayerAsync(functionSlug, targetRoom, playerId, new { team = "blue" }, ct);
    // Set from game networking. AdmissionId must identify the exact accepted connection, not just the player.
    public Action<string, string> DisconnectPlayer;
    // Optional operator support. Set before Start; callbacks must await actual networking/world cleanup.
    // Returning true immediately after queueing a disconnect would falsely confirm the operator action.
    public Func<PlayServParticipantRemoveContext, CancellationToken, Task<bool>> DisconnectOperatorParticipantAsync;
    public Func<PlayServOperatorRoomCloseContext, CancellationToken, Task<bool>> EndOperatorRoomAsync;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

    private async void Start()
    {
        try
        {
            var endRoom = EndOperatorRoomAsync;
            PlayServGameServer.Configure(new PlayServGameServerOptions
            {
                ExecutorSlug = functionSlug,
                ParticipantRemoveHandler = DisconnectOperatorParticipantAsync,
                RoomCloseHandler = endRoom == null ? null : (request, ct) => CloseForOperatorAsync(request, endRoom, ct)
            });
            PlayServGameServer.Uplink.OnError += error => Debug.LogWarning(error.SourceCode);
            PlayServGameServer.Uplink.StateChanged += state => Debug.Log("Uplink state: " + state);
            PlayServGameServer.Uplink.ConfigurationChanged += config => Debug.Log("Room config version: " + (config?.Version.ToString() ?? "unavailable"));
            PlayServGameServer.Analytics.Track(
                "server_starting",
                new Dictionary<string, object>
                {
                    ["function"] = functionSlug,
                    ["capacity"] = capacity
                });
            // StartRoomAsync requires admission:push and obtains platform configuration before registration.
            // Records realtime is a separate, optional /ws connection requiring its own sk_*.
            _room = await PlayServGameServer.StartRoomAsync(
                new PlayServStartRoomRequest(
                    functionSlug,
                    new PlayServGameRoomSnapshot(roomName, 0, capacity)),
                _lifetime.Token);
            _room.Admission.AdmissionRejected += rejection => DisconnectPlayer?.Invoke(rejection.PlayerId, rejection.AdmissionId);
        }
        catch (Exception exception)
        {
            Debug.LogError(exception.Message);
        }
    }

    private async Task<bool> CloseForOperatorAsync(PlayServOperatorRoomCloseContext request,
        Func<PlayServOperatorRoomCloseContext, CancellationToken, Task<bool>> endGameRoom, CancellationToken ct)
    {
        var closing = _room;
        // Invoke the real game cleanup even without a managed handle. Absence in SDK is not absence in gameplay.
        if (!await endGameRoom(request, ct)) return false;
        // Actual cleanup may finish after the budget. Release only this generation; SDK suppresses late replies.
        if (closing?.RoomName == request.RoomName && ReferenceEquals(_room, closing)) _room = null;
        // Do not re-create this room after room_closed_by_operator. No REST CloseAsync is needed here.
        return true;
    }

    public void SetPlayerCount(int players)
    {
        if (_room == null)
            return;
        _room.Update(new PlayServGameRoomSnapshot(roomName, players, capacity));
    }

    public PlayServRoomAdmissionResult Admit(string reservationToken)
    {
        if (DisconnectPlayer == null) throw new InvalidOperationException("Wire DisconnectPlayer to game networking before admission.");
        if (_room == null) throw new InvalidOperationException("Wait for room registration.");
        // The ticket supplies PlayerId. Store the returned AdmissionId on the game connection.
        // No HTTP consume or legacy opt-out. No second ReportJoin.
        // A negative/lost join_ack or disconnect while pending invokes DisconnectPlayer.
        return _room.Admission.TryAdmit(reservationToken);
    }

    // Optional online revocation/status check; reservation admission above is still required.
    // The game supplies the token in memory, never through serialized fields or logs.
    public Task<PlayServPlayerSessionVerificationResult> VerifySessionAsync(
        string playerId, string playerJwt, CancellationToken ct) =>
        PlayServGameServer.VerifyPlayerSessionAsync(playerId, playerJwt, ct);

    // Invoke at the game's removal decision, not during reconnect grace.
    public void ReportRemovedPlayer(string playerId) => _room?.ReportLeave(playerId);

    // Find remains algorithmic matchmaking; the result now includes endpoint metadata and an advisory TTL.
    public async Task<PlayServServerReservation> FindForPlayerAsync(string playerId, CancellationToken ct)
    {
        var result = await PlayServGameServer.FindMatchForPlayerAsync(new PlayServServerFindMatchRequest
        {
            FunctionSlug = functionSlug,
            PlayerId = playerId
        }, ct);
        if (!result.IsMatched) return null;
        var reservation = result.Reservation;
        if (reservation.RemainingLifetime == TimeSpan.Zero) return null;
        // A null lifetime or Connect is unknown, not an unlimited ticket or a ready endpoint.
        // Return Connect/Region/Attributes and Token to game-owned networking; never log Token.
        // The receiving server calls Admit against its pushed-ticket table, not this advisory TTL.
        return reservation;
    }

    public async Task StopServerAsync(CancellationToken cancellationToken)
    {
        var result = await PlayServGameServer.ShutdownAsync(cancellationToken);
        if (result.LogsError.IsError) Debug.LogWarning(result.LogsError.SourceCode);
    }

    // Pass the actual canonical project-scoped group, not a credential.
    public Task PublishRoundFinishedAsync(string group, int round, CancellationToken ct) =>
        PlayServGameServer.Events.PublishAsync(group, "round_finished", new { round }, reliable: true, ct: ct);

    // No storage acknowledgement: completion confirms send only.
    public Task SubmitFinalScoreAsync(string playerId, long score, string operationKey, CancellationToken ct) =>
        PlayServGameServer.Leaderboards.SubmitScoreAsync(playerId, score, idempotencyKey: operationKey, ct: ct);

    public Task<PlayServServerLeaderboardResult> ReadTopAsync(string viewerPlayerId, CancellationToken ct) =>
        PlayServGameServer.Leaderboards.GetTopAsync(10, viewerPlayerId, ct);

    // Explicit opt-in calls after uplink connection. Use schema names and dataflow keys unchanged.
    // Reacquire Uplink.Data for each call so a retained facade cannot follow a reconnect.
    public Task<PlayServUplinkDataResult<UplinkProgress>> ReadProgressAsync(
        string entityName, string dataflowKey, CancellationToken ct) =>
        PlayServGameServer.Uplink.Data.QueryAsync<UplinkProgress>(entityName, dataflowKey, ct);

    // Governed sends await accepted state; explicit ungoverned sends confirm transmission only.
    // Neither is storage durability. Use existing Records APIs for persistence/ETag semantics.
    // Player-owned creation needs acting-player attribution: use AsPlayer(...).Records<T>() instead.
    public Task SendProgressAsync(string entityName, string dataflowKey, UplinkProgress progress, CancellationToken ct) =>
        PlayServGameServer.Uplink.Data.SendUpsertAsync(entityName, dataflowKey, progress, ct);

    public Task SendCompletedMatchAsync(string entityName, string dataflowKey, CancellationToken ct) =>
        PlayServGameServer.Uplink.Data.SendIncrementAsync(entityName, dataflowKey,
            new Dictionary<string, long> { ["completed_matches"] = 1 }, ct);

    public Task SendProgressDeletionAsync(string entityName, string dataflowKey, CancellationToken ct) =>
        PlayServGameServer.Uplink.Data.SendDeleteAsync(entityName, dataflowKey, ct);

    // Keep Unknown/Pending outcomes and ResolveAsync on that same object, not a new increment.
    public Task<PlayServUplinkWriteOutcome> RecordCompletedMatchAsync(string entityName, string dataflowKey, CancellationToken ct) =>
        PlayServGameServer.Uplink.Data.IncrementWithOutcomeAsync(entityName, dataflowKey,
            new Dictionary<string, long> { ["completed_matches"] = 1 }, ct: ct);

    // One process-owned subscription can feed every room; no per-room polling or duplicated route.
    public async Task WatchProgressAsync(string entityName,
        Action<IReadOnlyDictionary<string, UplinkProgress>> applyCompleteSnapshot,
        Action<PlayServUplinkDataChange<UplinkProgress>> applyChange, CancellationToken ct)
    {
        if (_progress != null) throw new InvalidOperationException("Progress is already subscribed.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var subscription = await PlayServGameServer.Uplink.Data.SubscribeAsync<UplinkProgress>(entityName, "self", linked.Token);
        if (linked.IsCancellationRequested) { subscription.Dispose(); linked.Token.ThrowIfCancellationRequested(); }
        _progress = subscription;
        subscription.Changed += applyChange;
        subscription.Failed += error => Debug.LogWarning(error.SourceCode);
        subscription.StateChanged += state =>
        {
            if (state == PlayServUplinkSubscriptionState.Live) applyCompleteSnapshot(subscription.Snapshot);
        };
        if (subscription.IsComplete) applyCompleteSnapshot(subscription.Snapshot);
        // Observed means ungoverned delivered rows only; never present it as a complete table.
    }

    [Serializable]
    public sealed class UplinkProgress
    {
        [PlayServJsonName("completed_matches")]
        public long CompletedMatches { get; set; }
    }

    // Opt-in operational logs, separate from Analytics. No player credentials in log data.
    public bool QueueRoundLog(int round) =>
        PlayServGameServer.Logs.TryWrite("Round finished", data: new { round });

    public Task<PlayServServerLogFlushResult> FlushLogsAsync(CancellationToken ct) =>
        PlayServGameServer.Logs.FlushAsync(ct);

    private void OnDestroy()
    {
        _lifetime.Cancel();
        _progress?.Dispose();
        _room?.Dispose();
        _lifetime.Dispose();
    }
}
