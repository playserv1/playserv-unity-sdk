# PlayServ Game Server

`com.playserv.game-server` is the server-credential companion for Unity
Dedicated Server builds. It exposes server matchmaking, room registration and
heartbeats, reservation admission, server launch, room inspection, and a safe
player-profile lookup over the existing PlayServ runtime API. It also exposes
typed Records and Cloud Functions using the same rotating server credential.
Server Analytics, Catalog, and Storefront reads use that credential as well.

## Online player session verification

Launch metadata is opt-in: `PlayServServerLaunch.GetRoomName()` reads `PLAYSERV_ROOM_NAME`; `ResolveConnect(listenPort)` reads only `PLAYSERV_PUBLIC_IP` and `PLAYSERV_PORTS_MAPPING`. Explicit arguments win. Missing metadata returns null, malformed metadata throws, and neither helper starts a room or networking transport.

Configuration preflight: `await PlayServGameServer.GetRoomConfigurationAsync("arena", ct)` uses server-authorized REST without changing live room facts. Subscribe to `Uplink.StateChanged` and `Uplink.ConfigurationChanged` for ordered Unity-context notifications; configuration may become null on disconnect. Event arguments describe the transition, while getters can already reflect a later state.

Optional pushed-admission integration: create `room.CreateConnectionTracker()` (30-second grace), call `TryAdmit(connectionGenerationId, ticket)`, then `ReportDisconnected(connectionGenerationId)` on a transient drop. Use a new ID for every connection generation. `RemovePlayer(playerId)` is an immediate quit/kick. Handle `ConnectionRemoved` by disconnecting that exact connection in your game transport. Never combine manual presence with tracker-owned players. Legacy consume is not supported by this opt-in tracker.

Named-room reservation: `await PlayServGameServer.JoinRoomForPlayerAsync("arena", "room-one", playerId, new { team = "blue" }, ct)`.
This sends one `/rooms/...:join` request with server authorization. It does not find, launch or connect a room; admission params are snapshotted and must not contain secrets.

`await PlayServGameServer.VerifyPlayerSessionAsync(playerId, playerJwt, ct)`
uses `POST /players/sessions:verify` with the current server credential (or the
active uplink session). `Valid=false` and `Reason` are application verdicts,
including revoked, banned and suspended sessions; unknown reasons are preserved.
HTTP/transport failures throw `PlayServGameServerException`. Tokens are not
persisted or included in SDK diagnostics. This complements offline JWKS validation
and does not replace reservation admission (local tickets or legacy consume). Local fixture tests are not live
backend integration evidence.

## Server-to-client events

After explicit uplink connection (or managed room startup), use
`await PlayServGameServer.Events.PublishAsync("prj_ID:room", "round_finished", payload, reliable: true, ct: ct)`.
Use the canonical project-scoped group; the SDK never infers or rewrites it.
The backend enforces project ownership. The complete UTF-8 frame is limited to
1 MiB. Completion means socket send, not client receipt; reliable selects the
platform delivery lane and is not an acknowledgement. Disconnected calls fail
without connecting or queuing; reconnect never replays these events. Client
event-publishing restrictions are unchanged.

## Scores and leaderboard

`PlayServGameServer.Leaderboards.SubmitScoreAsync(playerId, score, metadata, idempotencyKey, ct)`
sends a signed 64-bit score. Metadata and caller-owned idempotency key are optional.
Completion is socket send only: this protocol has no score storage acknowledgement.
No automatic retry or reconnect replay is performed.

`await PlayServGameServer.Leaderboards.GetTopAsync(10, viewerPlayerId, ct)`
returns immutable entries (rank, player ID, nullable display name, score, recorded
timestamp). Top accepts 1–100; the viewer can appear as an additional entry beyond
Top. Queries use the configured HTTP timeout, unique correlation IDs and bounded
pending work; disconnect/cancellation removes pending requests. Both calls require
an existing uplink and never fall back to HTTP.

This is the **existing project-level best-score store**, not SDK V2 Leaderboards:
no separate board IDs, seasons, AroundMe window or guarantee of environment-isolated
ranking. These wrappers do not strengthen the backend's storage/idempotency guarantees.

## Structured logs

Logging is opt-in: call `PlayServGameServer.Logs.TryWrite(message, level, data)`
and explicitly `await PlayServGameServer.Logs.FlushAsync(ct)`. There is no global
`Debug.Log` capture, timer, reconnect replay or Analytics forwarding.
Entries are frozen and credential-redacted **before** enqueue. This is a credential
filter, not a general privacy filter: do not supply unrelated secrets or personal data.

`LogQueueCapacity` (default 256) and `LogQueueMaxBytes` (default 1 MiB) bound retained
UTF-8 frames, including the in-flight entry. A full queue rejects the new entry
with `false`; inspect `PendingCount`, `PendingBytes` and `DroppedCount`.
A frame must also fit the 1 MiB transport limit.

Concurrent flushes share one flight. Each caller can cancel its own wait; the
shared flush uses `HttpTimeout` as its total budget and includes only entries
queued at its start. The result reports sent, dropped, pending and a normalized
error. Without a connection, entries remain queued. An ambiguous attempted write
is dropped, not automatically retried; successful send is not a storage acknowledgement.
Shutdown makes a bounded best-effort flush before disconnect and reports additive
`LogsError` separately from room-close/Analytics outcomes. Reset/shutdown clears
the queue and counters.

## Installation

Install the core SDK and this package from the same tag or commit:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.game-server": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.game-server#<tag-or-commit>"
  }
}
```

Build the Unity player with the Dedicated Server target so `UNITY_SERVER` is
defined. Editor execution is supported for tests. In a normal client build the
facade rejects every operation before resolving credentials or starting HTTP.

## Configure

Set `PLAYSERV_API_URL` and `PLAYSERV_SERVER_KEY` in the server process, then
configure the runtime once:

```csharp
using Playserv.GameServer;

PlayServGameServer.Configure(new PlayServGameServerOptions());
```

For secret rotation, provide a resolver. Before uplink startup it runs for HTTP;
after startup it resolves the upgrade credential on each reconnect:

```csharp
PlayServGameServer.Configure(new PlayServGameServerOptions
{
    BackendServerAddress = "https://runtime.example.com",
    ServerKeyProvider = new PlayServDelegateServerKeyProvider(
        ct => secretStore.GetCurrentPlayServServerKeyAsync(ct)),
    HeartbeatInterval = TimeSpan.FromSeconds(5),
    HttpTimeout = TimeSpan.FromSeconds(10)
});
```

Never serialize `sk_*` into a scene, prefab, `ScriptableObject`, command-line
log, or source-controlled config. REST uses `Bearer sk_*` before uplink startup
or in explicit REST-only mode, and the in-memory session bearer after uplink
startup. A deployment token is sent only on the uplink upgrade. No public client
token or environment header is added; `AsPlayer` is the explicit acting-player exception.

## Dial-in uplink (PSV-2556 stage one)

Managed room hosting now opens `/uplink` before the first registration. Set
`PLAYSERV_EXECUTOR_SLUG` alongside `PLAYSERV_API_URL` and either
`PLAYSERV_DEPLOYMENT_TOKEN` (platform-started process) or `PLAYSERV_SERVER_KEY`
(studio-started process). Explicit options override the corresponding environment
values. `UplinkCredentialProvider` is upgrade-only; the existing `ServerKeyProvider`
remains independent for `/ws` Records. Configure on Unity's main thread so callbacks
are delivered through its synchronization context.

```csharp
PlayServGameServer.Configure(new PlayServGameServerOptions { ExecutorSlug = "arena" });
await PlayServGameServer.Uplink.ConnectAsync(ct); // no room needed
var room = await PlayServGameServer.StartRoomAsync(new PlayServStartRoomRequest(
    "arena", new PlayServGameRoomSnapshot("room-1", 0, 8,
        connect: new PlayServGameRoomConnect("game.example.com", 7777, "udp"), region: "eu")), ct);
// Push mode uses room.Admission.TryAdmit(token), which includes presence join.
// Legacy Consume mode only: room.ReportJoin(playerId) after successful consume.
room.ReportLeave("plr_example"); // game's removal decision, not a temporary socket drop
await room.CloseAsync(ct);      // uplink remains available with zero rooms
await PlayServGameServer.ShutdownAsync(ct);
```

One process session serves one executor and a stable random instance ID. A missing
`room_config` allows the connection but refuses room startup. Platform capacity,
reservation TTL, maximum rooms, lifetime and non-bot idle timeout are authoritative;
newer configuration versions apply live. Capacity shrink does not evict players or
invalidate already-issued tickets. `LifetimeClosed` and `PresenceDiverged` expose
timer shutdown and repeated roster repair failure. `Kick` reports a game-enforced
removal; it does not disconnect a networking-framework client itself.

Presence is join/leave plus a deduplicated, sorted non-bot roster checksum on
heartbeats. Full roster repair occurs only after reconnect or a checksum mismatch,
not periodically. Report actual admission/removal decisions even while disconnected;
reconnect repairs from current local state. Frame diagnostics never include raw
frames, credentials or server exception text.

The session token stays in memory and drives companion REST calls. A still-valid
session remains usable through a socket outage. The backend now emits renewal at
half-life; the SDK replaces its in-memory bearer and receipt-clock deadline without
reconnecting or resetting tickets. Missing renewal causes a fresh hello at expiry.
An expired bearer is never sent, and ambiguous REST mutations are not retried.
Terminal authentication/protocol refusal stops automatic reconnect and is exposed
through `Uplink.State`, `Uplink.LastError`, `OnError` and affected room handles.

Set `EnableUplink = false` to retain REST-only hosting, without presence. Ordinary
HTTP-only use does not implicitly connect. After explicitly disconnecting a started
uplink, reconnect it before further REST calls; there is no credential downgrade.

**Dated boundary, 2026-09-17:** Records realtime remains on its opt-in `/ws` connection
with a separate `sk_*` credential path; a deployment-only host needs an independent
server key to enable it. Existing Records CRUD, `SaveAsync` and bulk remain on REST;
the separate `Uplink.Data` API below does not migrate them.
Admission now supports the served PSV-2601 pushed-ticket protocol (see migration below).
Room routes use the served PSV-2600 namespace as described below;
no deprecated-route fallback is attempted. Inbound RPC is opt-in through the explicit
registry described below. This is not closure of every expanded PSV-2556 acceptance
criterion: mutation acknowledgements and instance/environment-safe data subscriptions remain outstanding.

## Explicit uplink data (PSV-2556)

**Unreleased, 2026-09-17:** backend PSV-2683 enables data operations on a dial-in
connection. Use `PlayServGameServer.Uplink.Data` only after opening the uplink.
`entityName` is the schema name, not an `ent_*` identifier. `key` is the backend's
dataflow key, not necessarily a REST record ID. Both are sent unchanged; the SDK
does not infer a primary key or rename fields. DTOs use the existing JSON codec
and serialization attributes.

```csharp
// Open the connection explicitly, or wait for managed room startup to finish.
await PlayServGameServer.Uplink.ConnectAsync(ct);
var data = PlayServGameServer.Uplink.Data;
var snapshot = await data.QueryAsync<ArenaProgress>("ArenaProgress", progressKey, ct);
if (snapshot.Found)
    ShowCompletedMatches(snapshot.Value.CompletedMatches);

// Independent, explicit game decisions; each completion confirms send ONLY.
await data.SendUpsertAsync("ArenaProgress", progressKey,
    new ArenaProgress { CompletedMatches = 3 }, ct);
await data.SendIncrementAsync("ArenaProgress", progressKey,
    new Dictionary<string, long> { ["completed_matches"] = 1 }, ct);
await data.SendDeleteAsync("ArenaProgress", progressKey, ct);
```

Here `ArenaProgress` is a game-owned DTO for a non-player-owned table, `progressKey` is a dataflow key,
and `ShowCompletedMatches` is game UI, not an SDK member:

```csharp
public sealed class ArenaProgress
{
    [Playserv.Serialization.PlayServJsonName("completed_matches")]
    public long CompletedMatches { get; set; }
}
```

`PlayServUplinkDataResult<T>` exposes only `Entity`, `Key`, `Found` and `Value`.
It is not a `PlayServRecord<T>` and has no ETag, timestamps or persistence status.
`Found=false` may mean a missing record **or a refusal hidden by the backend**;
`Value` is then `default(T)`. Do not interpret it as proof that a write may create a row.

Upsert requires a JSON object and snapshots it before asynchronous work. Increment
accepts explicit field-name/`long` deltas; singleton increment and delete are refused
locally. Singleton query/upsert also accept the contract's empty string key; it is
sent unchanged. Ordinary tables require a non-empty key. The server catalogue is
isolated per connection, released on disconnect, and never inherited from REST
credentials or an older connection. Its pre-check uses the current uplink session and refreshes
once before rejecting a known read/write denial. Unknown ACL flags are advisory,
not an authorization grant. A schema absent from the visible catalogue is refused;
this does not prove physical absence outside the caller's visibility.

Each facade is bound to one connection: **reacquire `Uplink.Data` after reconnect**.
Queries have unique request IDs, the configured `HttpTimeout` deadline and a limit
of 128 pending calls. Frames in both directions are capped at 1 MiB. Cancellation,
connection loss, termination, reconfiguration and shutdown complete pending calls;
late replies cannot satisfy a new connection's requests. Operational failures use
`PlayServGameServerException` / `UnifiedError`; caller cancellation remains cancellation.

No call implicitly connects, retries, queues offline, or falls back to REST mutations.
`Send*Async` completion is **not a write acknowledgement**. Cancellation after send
begins does not roll back a possible write. Credentials and caller-selected
project/environment scope are not added to data frames; scope belongs to the connection.
There is no acting-player overload in this increment. Creating a row in a
player-owned table requires that attribution on the backend and is not supported
by these send-only methods; the send may complete even though the backend refuses
the creation. Use existing player-scoped `AsPlayer(...).Records<T>()` for that workflow.

**Known server boundary:** the current uplink write path does not enforce the table
Write flag. The SDK's local catalogue check is not a security boundary and cannot
replace server enforcement. Keep confirmed/ETag-aware mutations on existing Records
APIs when those guarantees are needed. `subscribe_data` / `data_update` are deliberately
not exposed until routing is safe across instances and environments. Mutation
acknowledgements and that routing remain PSV-2556 dependencies. Tests for this
increment use real serialization/dispatch with fake transports, not live backend integration.

## Pushed-ticket admission — default change, Unreleased 2026-09-13

`EnablePushedAdmission` defaults to **true**. The hello advertises `admission_push`;
`Uplink.AdmissionMode` reports the platform-confirmed `Push` or `Consume` path.
The SDK stores offers before answering `ticket_result`. The optional
`TicketOfferHandler` receives a defensive snapshot on Unity's configured context;
null accepts valid offers. Finish within three seconds and observe cancellation.
The receive loop remains free to process pings. Hook exceptions and late decisions
do not invent wire refusal codes or expose exception text.

```csharp
PlayServGameServer.Configure(new PlayServGameServerOptions
{
    ExecutorSlug = "arena",
    TicketOfferHandler = (offer, ct) => Task.FromResult(PlayServTicketDecision.Accept())
});
// After StartRoomAsync has returned a room:
room.Admission.AdmissionRejected += rejected =>
    gameNetwork.DisconnectAdmission(rejected.AdmissionId); // game-owned connection mapping
var admission = room.Admission.TryAdmit(reservationToken); // synchronous PreLogin
if (!admission.Ok) return; // refuse the game connection
gameNetwork.Accept(admission.PlayerId, admission.AdmissionId);
```

`gameNetwork` above is illustrative game integration, not an SDK networking API.
Player identity comes from the offer, not a caller-supplied player ID. Subscribe
to `AdmissionRejected` **before** accepting connections and identify the exact
connection by `AdmissionId`; a delayed rejection must not disconnect a newer one.
Successful `TryAdmit` queues one token-bearing join without waiting for `join_ack`.
Do **not** also call `ReportJoin` or REST consume. Negative ack, ack timeout,
ambiguous send or disconnect while ack is pending revoke the local admission.
The SDK updates its roster and signals the game; it cannot disconnect your transport.

Use `room.Admission.ReleaseAsync(token, "room_refused")` for a local refusal before
admission. No automatic send replay is performed. The table expires tickets using
a monotonic clock, clears them with the room and rejects replay. Unused tickets
survive a temporary disconnect until their TTL, but new admission requires a live
uplink. Entry and serialized-byte limits default to 4096 and 4 MiB per process;
configure `AdmissionEntryLimit` / `AdmissionByteLimit` if needed. Limit overflow
refuses new offers rather than evicting a ticket already promised to a player.

**Migration:** existing `ConsumeReservationAsync` + `ReportJoin` integrations must
either switch to the API above or configure `EnablePushedAdmission = false`.
`EnableUplink = false` retains REST-only hosting. A legacy backend that negotiates
`Consume` still requires the old admission path; no hidden HTTP fallback is made
by `TryAdmit`. In negotiated `Push`, legacy consume and new tokenless joins fail
locally before HTTP or roster mutation. This checkout prepares package version 0.6.7; publication is separate.
Never log reservation tokens, credentials or arbitrary offer parameters.

## Served room routes and reservation lifetime (PSV-2602)

**Unreleased, 2026-09-12:** list uses `GET /rooms/{slug}`, registration and heartbeat
use `POST /rooms/{slug}:upsert`, close uses `POST /rooms/{slug}/{roomName}:close`,
and server/client launch uses `POST /rooms/{slug}/servers:launch`. Managed room
startup and the platform-requested room factory share this registration path.
The algorithmic `matchmaking/{slug}/find` and
`matchmaking/{slug}/reservations/{token}:consume` routes are unchanged.
PSV-2600 is implemented in the backend; fixture tests do not prove live integration.

`FindMatchForPlayerAsync` reservations include nullable `Connect`, `Region` and
`Attributes`. Endpoint fields are passed through without DNS or networking setup;
attributes are a snapshot, copied on each access. `ExpiresIn` is nullable integer
seconds from response time. `RemainingLifetime` counts down on a monotonic clock,
floored at zero; missing/null TTL means unknown on older backends, not unlimited.
Do not compare the retained `ExpiresAt` timestamp with a local wall clock. Invalid
TTL types or ranges return a typed `InvalidResponse`. Admission remains authoritative.

In negotiated Consume mode, `ConsumeReservationAsync` continues returning `Ok=false` with `ErrorCode` for
`room_closed`, `reservation_expired`, `reservation_consumed`, `reservation_invalid`
or `room_mismatch`, and preserves unknown future codes. HTTP failures still throw
`PlayServGameServerException`; `room_type_not_found` and Problem Details remain in
`UnifiedError.SourceCode` and credential-filtered `RawDetails`. Never log reservation tokens.

## Create rooms on platform request (PSV-2597 SDK-side)

Configure a game-owned `RoomFactory` and connect the uplink with **zero initial rooms**:

```csharp
PlayServGameServer.Configure(new PlayServGameServerOptions
{
    ExecutorSlug = "arena",
    RoomFactory = (request, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        // Prepare the local game here; do not call StartRoomAsync/UpsertRoomAsync inside the factory.
        return Task.FromResult(PlayServRoomCreateDecision.Accept(
            new PlayServGameRoomSnapshot(request.RoomName, 0, request.Configuration.Capacity,
                connect: new PlayServGameRoomConnect("game.example.com", 7777, "udp"))));
    }
});
PlayServGameServer.Uplink.RoomCreationCompleted += outcome =>
{
    if (outcome.IsSuccess) AttachGameToRoom(outcome.Room);
    else if (outcome.FactoryInvoked) CleanupLocalPreparation(outcome.RoomName);
};
await PlayServGameServer.Uplink.ConnectAsync(ct);
```

Only a configured factory advertises `room_create`; the independent `rpc`
capability requires a configured RPC registry. The factory runs on the captured Unity context, outside
the receive loop. It must preserve the requested name, provide `Connect`, honor
cancellation, and clean up local resources if preparation/registration fails.
`PlayServRoomCreateDecision.Refuse(...)` supports all five contract reasons:

| Enum value | Wire reason | Meaning |
|---|---|---|
| `RoomNameConflict` | `room_name_conflict` | The name is already active or pending. |
| `InstanceDraining` | `instance_draining` | The instance is no longer accepting new rooms. |
| `RoomQuotaExceeded` | `room_quota_exceeded` | Active plus pending rooms have reached `max_rooms`; capacity may become available after a room closes. |
| `RoomCreateFailed` | `room_create_failed` | The room factory failed for this request; the instance remains a candidate. |
| `ContentRefused` | `content_refused` | The game declined the requested attributes; it can still accept later requests with different content. |

Only `instance_draining` tells routing to remove the instance from future selection;
quota, factory and content refusals do not stop it accepting later requests. Optional
game-provided refusal detail is limited to 128 non-control characters; never put secrets there.
A thrown/faulted factory produces `room_create_failed` with only the short exception
type name, truncated to 128 characters. No exception message, stack, inner exception
or payload is published. Local diagnostics keep `room_factory_failed` and
`RoomCreationCompleted`, and release the reserved name. Independent factory cancellation
is a factory failure while the SDK request is still active; SDK cancellation/timeout
does not send a late result. Invalid factory results and registration failures are
not reported as this factory-exception refusal, and a failed upsert after `ok: true`
never produces a second result.

### Requested attributes (PSV-2628)

`request.Attributes` is an independent JSON-object snapshot of the optional
`room_create.attributes`. Missing or `null` means `null`; `{}` stays an empty object.
The callback signature is unchanged: existing factories may ignore this additive
property. The incoming object must be one level of scalar JSON values and serialize
to at most 2048 UTF-8 bytes. Malformed attributes are dropped with the safe local
`room_create_invalid_request` diagnostic, without invoking the factory or registering a room.

Attributes are a player's **wish**, not trusted room configuration. The game may
apply them, replace them or return `Refuse(ContentRefused, "Unsupported map")` before
preparing local resources. On refusal, the SDK releases the name and reports
`RoomCreationCompleted`; game-owned cleanup remains necessary if preparation already started.
Never put the requested content, credentials or raw exception text in refusal details.

Only the attributes in the factory's returned `PlayServGameRoomSnapshot` are sent
by upsert/heartbeat. Nothing is automatically copied or merged from the wish:

```csharp
// Inside the existing RoomFactory. A game may choose a different default/map policy.
var wish = request.Attributes as IDictionary<string, object>;
var map = wish != null && wish.TryGetValue("map", out var value) ? value as string : "arena";
if (map != "arena" && map != "forest")
    return Task.FromResult(PlayServRoomCreateDecision.Refuse(
        PlayServRoomCreateRefusal.ContentRefused, "Unsupported map"));
return Task.FromResult(PlayServRoomCreateDecision.Accept(new PlayServGameRoomSnapshot(
    request.RoomName, 0, request.Configuration.Capacity,
    attributes: wish == null ? null : new { map },
    connect: new PlayServGameRoomConnect("game.example.com", 7777, "udp"))));
```

See `PlayServRequestedRoomServerSample` for apply/alter/refuse handling with an
explicit map/mode allowlist. Manual `StartRoomAsync` and existing factories do not
acquire a new automatic content policy.

### Registration and shutdown

`AcceptingRoomRequests = false` refuses new requests without invoking the factory.
Manual and requested starts share one name/quota guard. On acceptance the SDK sends
`room_create_result` and runs its existing room registration path. Default total
response/registration budget is five seconds; align `RoomCreateTimeout` with the
platform setting if it changes. Timeout never produces a late success or starts a
late HTTP request. Cancellation cannot roll back an already-dispatched upsert:
an ambiguous registration retains the local name reservation until shutdown, and
neither a repeated frame nor a local start retries it automatically. Resolve the
remote state before intentionally restarting/reusing that name.

An early duplicate refusal has `FactoryInvoked = false`: do not clean up the other
request's active preparation. `ShutdownAsync` stops new requests, cancels/drains
pending factories, closes managed rooms and finally closes the uplink. A callback
that ignores cancellation cannot stall shutdown, but remains responsible for its
own game-local cleanup.

**Availability:** backend senders are implemented in `dev`: player hosting
(`POST /rooms/{slug}:host`, PSV-2626) and platform-requested creation (PSV-2590).
This companion reads the optional attributes. The core SDK's player-facing
`PlayServMatchmaking.HostRoomAsync` (PSV-2694) initiates the other end of this flow;
see the core README's **Host a room** example. Its request attributes are a wish:
only the factory's returned snapshot determines registered values. Host returns
the hosting player's reservation, without another client Join or server launch.
The companion factory API and admission behavior are unchanged. SDK HTTP fixtures
do not establish a deployed Host → factory → admission integration.
Verify the deployed environment and the game's factory together before claiming
end-to-end readiness. SDK fixtures are not live platform-router verification.
Admission state is installed before registration HTTP starts: the served backend
can send pending ticket offers and wait for their answers before returning upsert.
Fixtures exercise this ordering together with `room_create` and renewed REST credentials.

## Server Records and Cloud Functions

Server Records reuse the core typed handles, ETags, query builder, projections,
expansion, and singleton API. Access checks use `acl.server`, and server reads
are not restricted to the current player-owner scope:

```csharp
var jobs = PlayServGameServer.Records<MatchJob>();
var page = await jobs.QueryAsync(
    new PlayServRecordQuery<MatchJob>()
        .Where(x => x.State == "queued")
        .WithLimit(50));

var job = await jobs.LoadAsync("rec_01...");
job.Value.State = "assigned";
await job.SaveAsync(); // sends the handle's current ETag
```

Use an explicit schema ID when the CLR name differs from the PlayServ table:

```csharp
var jobs = PlayServGameServer.Records<MatchJob>("ent_01...");
```

`PlayServGameServer.GetTablesAsync()`, `GetTableAsync(idOrName)`, and
`RefreshTablesAsync()` expose the same server-scoped cached catalogue used by
Records resolution, including table metadata and complete ACL capabilities.

Player-owned records must be created with an acting player session JWT. The
context sends that JWT only on Records writes; reads retain the dedicated
server's normal owner-bypass scope:

```csharp
var inventory = PlayServGameServer
    .AsPlayer(playerAccessToken)
    .Records<PlayerInventory>();

var created = await inventory.CreateAsync(new PlayerInventory());
```

The JWT is memory-only and is never logged. PlayServ validates its signature,
project, environment, session lifetime, and the target table on the backend.

Cloud Functions support the same request methods, query, typed JSON calls,
tagged versions, timeouts, and safe custom headers as the player facade:

```csharp
var result = await PlayServGameServer.Code.CallAsync<AllocateRequest, AllocateResponse>(
    "allocate-room",
    new AllocateRequest { Region = "eu" });
```

These APIs never copy `sk_*` into `PlayServ.Settings`, never send
`X-PlayServ-Client`, and resolve the current server key for every request.

## Analytics and commerce

Server analytics reuses the typed parameters, bounded queue, batching, and
retry-safe flush behavior of `com.playserv.analytics`. Attribute a player on
each event instead of changing shared process state:

```csharp
PlayServGameServer.Analytics.Track(
    "match_completed",
    new Dictionary<string, object>
    {
        ["mode"] = "ranked",
        ["duration_seconds"] = 412
    },
    playerId: "plr_...");

await PlayServGameServer.Analytics.FlushAsync();
```

`PlayServGameServer.Catalog` and `PlayServGameServer.Storefronts` expose the
same typed query, page, item, and storefront models as the player runtime while
sending only the rotating `sk_*` bearer:

```csharp
var items = await PlayServGameServer.Catalog.ListAsync(
    new PlayServCatalogQuery { Status = "active", Limit = 100 });
var storefront = await PlayServGameServer.Storefronts.GetAsync("sf_...");
```

`ShutdownAsync` attempts to flush queued analytics before clearing server
configuration. A failed flush is returned as `AnalyticsError`; room close
results remain available independently.

### Realtime Records

Realtime is opt-in and uses an independent WebSocket session, so it never
reuses or changes a player SDK connection. Connect it before subscribing a
server Records handle:

```csharp
await PlayServGameServer.Realtime.ConnectAsync(
    new PlayServGameServerRealtimeOptions
    {
        InstanceId = roomProcessId,
        GameVersion = Application.version
    });

var jobs = PlayServGameServer.Records<MatchJob>();
var liveQueue = await jobs.SubscribeAsync(
    new PlayServRecordQuery<MatchJob>().Where(x => x.State == "queued"));
var liveJob = await (await jobs.LoadAsync("rec_01...")).SubscribeAsync();
await liveQueue.RefreshAsync();
```

The handshake contains `Authorization: Bearer sk_*` and no public client key,
player JWT, legacy game ID, or user ID. The key provider is called again on reconnect, active handles
use the standard refcount/replay/refresh/close lifecycle, and
`ShutdownAsync` closes the realtime session.

## Room lifecycle

`StartRoomAsync` performs the first registration before returning and then owns
an independent single-flight heartbeat loop for that room:

```csharp
var room = await PlayServGameServer.StartRoomAsync(
    new PlayServStartRoomRequest(
        "ranked-arena",
        new PlayServGameRoomSnapshot(
            "eu-17",
            players: 0,
            capacity: 16,
            state: "lobby",
            attributes: new { map = "forest" })));

room.Update(new PlayServGameRoomSnapshot(
    "eu-17",
    players: connectedPlayers,
    capacity: 16,
    state: "playing",
    attributes: new { map = "forest" },
    open: false));
```

Transient network, timeout, `429`, and `5xx` failures put the handle into
`Degraded` and are retried. Authorization, validation, and other terminal
failures put it into `Terminated`. Backend `draining` and `open_refused`
acknowledgments change placement state without modifying the game's desired
snapshot.

Call `CloseAsync` for one room and `ShutdownAsync` for graceful process
shutdown. `Dispose` is only best-effort. Unity application quit cancels the
loops and starts a short cleanup, but a host should await `ShutdownAsync` before
exiting whenever possible.

## Matchmaking and admission

Server matchmaking accepts an explicit player ID and JSON-object lobby state.
The request timeout is derived from the same `wait_ms`: ten seconds for an
immediate request, otherwise `wait_ms + 5 seconds`.

After the game's own network handshake receives a reservation credential, pass
it to `room.Admission.TryAdmit` in Push mode; use `ConsumeReservationAsync` only
in the negotiated legacy Consume mode. The package deliberately has no dependency on
Netcode for GameObjects, Mirror, Photon, or another networking framework. It
does not log or persist reservation credentials.

For an early, offline admission check, validate the player's session token
against PlayServ's public signing keys:

```csharp
var validation = await PlayServGameServer.ValidatePlayerTokenAsync(
    playerJwt,
    new PlayServPlayerTokenValidationOptions
    {
        ExpectedProjectId = "prj_...",
        ExpectedEnvironment = "prod"
    });

if (!validation.IsValid)
    Reject(validation.UnifiedError.SourceCode);
```

Validation accepts only RS256, checks signature, `kid`, issuer, lifetime,
project and environment, and caches `/.well-known/jwks.json` according to
`Cache-Control`. It never checks revocation, so reservation consumption and
backend admission remain authoritative.

`GetPlayerAsync` returns only ID, name, status, SSO providers, country, joined,
and timestamps. IP addresses, fingerprints, and moderation details are never
exposed by this package.
Runtime schema presence can be checked explicitly with `PlayServGameServer.CheckSchemaAsync(types, ct)`. A table missing from the caller-visible catalogue may be ACL-hidden; this does not validate field compatibility or modify schema.
### Explicit inbound RPC

Set `PlayServGameServerOptions.RpcRegistry` before `Configure` to advertise `rpc`.
Register typed `PlayServServerRpcParameter<T>` descriptors and `Register<TResult>`
handlers, using the same descriptor instances with `args.Get(parameter)`.
Positional JSON arrays and named JSON objects are strictly bound; missing required,
unknown or incorrectly typed arguments never reach game code. See
`Samples~/DedicatedServer/PlayServServerRpcSample.cs` for explicit generic and
`[Preserve]` registrations suitable for IL2CPP; preserve DTO constructors and members.

Callbacks use the synchronization context captured by `Configure` (call it from
Unity's main thread). The registry is snapshotted at configuration; no assembly scan,
dynamic compilation or implicit RPC capability exists. `room_create` is independent.

Calls run serially, with at most 64 retained calls and 1 MiB input including the active
call. `HttpTimeout` starts at receipt, including queue time. Handlers must cooperate
with cancellation: a handler ignoring it keeps the serial slot and memory charge
until it returns. Timeout still returns a safe error on time. No remote rollback,
exactly-once execution or replay is promised. A repeated active ID does not invoke
the handler twice; completed IDs are not an exactly-once ledger.

`one_way` never returns a result. Disconnect/shutdown cancel local tokens; late
results cannot be sent on a replacement connection. Caller metadata comes only
from authenticated platform frames. `player_jwt` is not retained or installed as
REST/Records credentials. Games must still authorize their own operations.
Diagnostics omit payloads, credentials and game exception messages.
