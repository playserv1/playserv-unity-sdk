# PlayServ Game Server

`com.playserv.game-server` is the server-credential companion for Unity
Dedicated Server builds. It exposes server matchmaking, room registration and
heartbeats, reservation admission, server launch, room inspection, and a safe
player-profile lookup over the existing PlayServ runtime API. It also exposes
typed Records and Cloud Functions using the same rotating server credential.
Server Analytics, Catalog, and Storefront reads use that credential as well.

## Launch metadata

Launch metadata is opt-in: `PlayServServerLaunch.GetRoomName()` reads `PLAYSERV_ROOM_NAME`; `ResolveConnect(listenPort)` prefers `PLAYSERV_PUBLIC_HOST`, then `PLAYSERV_PUBLIC_IP`, and reads `PLAYSERV_PORTS_MAPPING`. Explicit arguments win. Missing metadata returns null, malformed metadata throws, and neither helper starts a room or networking transport.

## A platform-launched room

For one server process assigned one room, call `StartHostedRoomAsync` on Unity's main thread, before manual `Configure`. It validates the launch, connects the uplink, waits for the game's listener to be ready and registers the room. The returned handle means registration was acknowledged, not merely that the socket opened.

```csharp
var hosted = await PlayServGameServer.StartHostedRoomAsync(gameTransport,
    new PlayServHostedRoomOptions { Transport = "udp" }, startupCancellation);
var room = hosted.Room;
// Use the existing pushed-ticket admission API before accepting gameplay.
await hosted.StopAsync();
```

`gameTransport` implements `IPlayServHostedRoomTransport`:

- `StartAsync(launch, ct)` starts the game's listener on `launch.ListenPort` and completes only when it is ready. `launch.Connect` is the advertised endpoint; its mapped public port can differ from the local listen port.
- `StopAsync(ct)` must be idempotent, stop any partial startup and honor cancellation. Both callbacks run on the Unity context captured at startup. If Start ignores cancellation and completes late, the SDK invokes Stop again before allowing another launch.

The SDK captures `PLAYSERV_API_URL`, `PLAYSERV_EXECUTOR_SLUG`, `PLAYSERV_ROOM_NAME`, `PLAYSERV_ROOM_LISTEN_PORT` and the room's JSON `PLAYSERV_ROOM_ATTRIBUTES` (absent means `{}`). It prefers `PLAYSERV_PUBLIC_HOST` over legacy `PLAYSERV_PUBLIC_IP`. Attributes must be a single-level object of scalar values, at most 2048 UTF-8 bytes. The listener receives an immutable launch snapshot with copied attributes. Capacity and heartbeat configuration remain platform-authoritative.

The credential comes from `PLAYSERV_DEPLOYMENT_TOKEN`, or, when absent, `/run/playserv/deployment-token`. Hosted startup never falls back to a studio key. The token's deployment identity supplies `instance_id`; `PLAYSERV_DEPLOYMENT_ID`, if present, must agree. This local shape check does not authenticate the token: the platform validates it on upgrade and issues the session bearer used for registration and heartbeats. Reconnect reads the credential source again and rejects a changed deployment identity. No credential is serialized into the launch snapshot, an Asset or a log.

`Transport` is the game's actual advertised protocol (`udp`, `tcp`, `ws` or `wss`; default `udp`). A matching protocol mapping takes precedence; WebSockets can also use TCP mappings. Selecting `wss` does not create a TLS listener or certificate. `ServerOptions` can supply the existing RPC, admission and operator handlers; conflicting identity settings, an alternate uplink credential, a disabled uplink or a multi-room factory are rejected.

Startup has a 60-second default deadline. Cancellation applies to startup; after success, await `hosted.StopAsync()` or `PlayServGameServer.ShutdownAsync()`. Stop uses a fresh cleanup budget (default five seconds for each cleanup phase), independent of the cancelled startup token. Room close, terminal removal and process shutdown also stop the owned listener. Keep the handle and listener alive across scene changes.

Only one hosted operation owns the process. Concurrent starts, manual reconfiguration and another managed room start are refused. A lost registration response is reported as `room_registration_outcome_unknown`; it never produces a success handle or an automatic retry. Restart the process after that outcome or an unconfirmed cleanup. A new process is also required if a listener never settles after cancellation. Server-side expiry still applies when a remote close cannot be confirmed.

See `Samples~/DedicatedServer/PlayServHostedServerSample.cs` for a minimal UDP binding example to replace with your game's transport.

## Room helpers and player session verification

Configuration preflight: `await PlayServGameServer.GetRoomConfigurationAsync("arena", ct)` uses server-authorized REST without changing live room facts. Subscribe to `Uplink.StateChanged` and `Uplink.ConfigurationChanged` for ordered Unity-context notifications; configuration may become null on disconnect. Event arguments describe the transition, while getters can already reflect a later state.

Optional connection tracking: create `room.CreateConnectionTracker()` (30-second grace), call `TryAdmit(connectionGenerationId, ticket)`, then `ReportDisconnected(connectionGenerationId)` on a transient drop. Use a new ID for every connection generation. `RemovePlayer(playerId)` is an immediate quit/kick. Handle `ConnectionRemoved` by disconnecting that exact connection in your game transport. Never combine manual presence with tracker-owned players. All reservation admission uses pushed tickets, with or without this tracker.

### Optional game network adapter

`PlayServRoomNetworkAdapter` subscribes to a game's `IPlayServRoomNetworkLifecycle`
and owns the existing tracker for one room. Attach it on Unity's main thread after
`StartHostedRoomAsync` or `StartRoomAsync` returns:

```csharp
var adapter = new PlayServRoomNetworkAdapter(room, gameNetwork,
    reconnectGrace: TimeSpan.FromSeconds(30));
adapter.Error += error => HandleNetworkingFailure(error.SourceCode);
```

The lifecycle source raises `Authenticating(connection, ticket)` and
`Disconnected(connection)`. A connection implements `IPlayServRoomNetworkConnection`:
`Accept(verifiedPlayerId)` enables gameplay, while `Close(reason)` disables gameplay
and closes **that exact connection** before returning. Keep gameplay disabled while
waiting for Accept, including before an adapter is attached. Allocate a new connection
object for each physical connection; never recycle it for a reconnect. The adapter
generates its own connection ID, obtains PlayerId from the verified ticket, and
ignores duplicate callbacks for the same object. It does not retain ticket strings.

Network events may originate on workers; Accept, Close and Error are delivered on
the captured Unity thread. Call the adapter's public methods on that thread.
`TryGetPlayerId(connection, out playerId)` reads the current active binding.
Local admission permits gameplay without waiting for `join_ack`, as the wire
contract specifies. A late platform refusal closes only the matching admission.

A disconnect parks the existing seat for grace; repeated disconnects do not extend
it. Reconnect uses a fresh ticket and a new connection object. An old disconnect or
refusal cannot remove the replacement. `RemovePlayer(playerId)` removes presence
immediately and closes the live connection; its bool reports presence removal,
not transport confirmation. An exception from Accept rolls back that admission,
even if Accept re-entered a disconnect callback. A callback failure exposes only a
safe code through `Error` and `LastError`; a `network_close_failed` must be handled
by the game and is not a confirmed disconnect. One failing Close does not prevent
cleanup of other connections.

Keep the adapter and transport with the room, for example on a root object using
`DontDestroyOnLoad`. A scene change that preserves the connection needs no new
admission and sends no extra join/leave. A real network loss uses the same grace and
fresh-ticket reconnect policy. `Dispose` detaches subscriptions, removes tracked
presence and closes live connections; room close and module shutdown also detach
an empty adapter. Do not create a second tracker or mutate presence manually for
these players. The manual tracker API remains available when no adapter is attached.

Existing operator handlers still own actual game disconnection/room teardown and
their confirmation. Operator room close detaches the adapter without a second
transport close; the configured handler must still close all real connections.
Do not use the adapter's removal bool as an operator cleanup acknowledgement.

Import **TCP Network Lifecycle** from Package Manager for a runnable, bounded TCP
example, client component and loopback tests. Its wire format is sample-specific;
the adapter itself adds no networking-library dependency.

Named-room reservation: `await PlayServGameServer.JoinRoomForPlayerAsync("arena", "room-one", playerId, new { team = "blue" }, ct)`.
This sends one `/rooms/...:join` request with server authorization. It does not find, launch or connect a room; admission params are snapshotted and must not contain secrets.

`await PlayServGameServer.VerifyPlayerSessionAsync(playerId, playerJwt, ct)`
uses `POST /players/sessions:verify` with the current server credential (or the
active uplink session). `Valid=false` and `Reason` are application verdicts,
including revoked, banned and suspended sessions; unknown reasons are preserved.
HTTP/transport failures throw `PlayServGameServerException`. Tokens are not
persisted or included in SDK diagnostics. This complements offline JWKS validation
and does not replace local pushed-ticket admission. Local fixture tests are not live
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

Manual logging uses `PlayServGameServer.Logs.TryWrite(message, level, data)`
and `await PlayServGameServer.Logs.FlushAsync(ct)`. Engine capture is disabled by
default. Enable it in the process configuration (also accepted through hosted
`ServerOptions`):

```csharp
PlayServGameServer.Configure(new PlayServGameServerOptions
{
    ExecutorSlug = "arena",
    EngineLogs = new PlayServEngineLogOptions
    {
        Enabled = true,
        MinimumLevel = PlayServServerLogLevel.Info,
        IncludeStackTrace = true
    }
});
```

Configure the server on Unity's main thread. The explicit server configuration
enables capture in `UNITY_SERVER` and Editor server tests; an ordinary player build
never installs the log subscription or delivery loop, even with `Enabled = true`.
The options are copied at configuration time. To change them, use the existing
reconfiguration lifecycle: close rooms and disconnect the uplink, then Configure.
An invalid replacement configuration leaves the current capture intact.
Enabling capture from a worker is rejected before changing the configuration.

Capture listens to `Application.logMessageReceivedThreaded`, so worker-thread
messages need no main-thread dispatch queue. `Debug.Log` maps to `info`, warnings
to `warn`, and Error/Exception/Assert to `error`. A minimum severity filters before
queueing. Error stacks are included when requested, within the same text budget.
The forwarder never writes its failures to `Debug.Log`; an async-flow guard also
excludes diagnostics emitted by its serialization/send path without suppressing
unrelated gameplay logs.

Entries are frozen and credential-redacted **before** enqueue. This is a credential
filter, not a general privacy filter: do not supply unrelated secrets or personal data.
Engine messages and stacks use the existing credential filters, extended for `rsv=`
query values, reservation/refresh tokens and named launch credentials. Text is cut
only after redaction, at a valid UTF-8 boundary. Raw engine text is not retained in
an intermediate queue.

| Engine capture setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` | Subscribe and automatically flush while this server configuration is active |
| `MinimumLevel` | `Info` | Lowest captured severity |
| `IncludeStackTrace` | `true` | Include error/exception/assert stacks |
| `FlushInterval` | 250 ms | At least 1 ms; interval between attempts, never overlapping automatic flights |
| `RateWindow` / `MaxEntriesPerWindow` | 10 s / 200 | Monotonic fixed window opened by the first eligible entry; excess entries are dropped |
| `MaxTextBytes` | 16 KiB | Combined UTF-8 message and stack budget, with message taking priority |

`LogQueueCapacity` (default 256) and `LogQueueMaxBytes` (default 1 MiB) bound retained
UTF-8 frames, including the in-flight entry. A full queue rejects the new entry
with `false`; inspect `PendingCount`, `PendingBytes` and `DroppedCount`.
A frame must also fit the 1 MiB transport limit.
Captured and manually written entries share this FIFO and its limits. When capture
is enabled, its periodic flush sends both kinds. No connection is opened by the
logger: normal server startup owns the uplink, and retained entries can be sent
after it connects or reconnects.

Inspect `Logs.EngineCapture` for `IsCapturing`, `AcceptedCount`, `FilteredCount`,
`RateLimitedCount`, `QueueRejectedCount`, `FailedCount` and the secret-safe `LastError`.
`AcceptedCount` means enqueued, not delivered. `FailedCount` counts capture/loop
failures; transport failures are in `LastError`, while ambiguous-send and FIFO
overflow drops for both manual/captured entries are in `Logs.DroppedCount`.
Counters are cumulative within the configuration. Read these diagnostics directly;
printing every counter update to the engine log would itself create more log traffic.

Concurrent flushes share one flight. Each caller can cancel its own wait; the
shared flush uses `HttpTimeout` as its total budget and includes only entries
queued at its start. The result reports sent, dropped, pending and a normalized
error. Without a connection, entries remain queued. An ambiguous attempted write
is dropped, not automatically retried; successful send is not a storage acknowledgement.
Graceful shutdown detaches capture before flushing. It drains even entries added
after an earlier shared flush began, within one total `HttpTimeout` budget (or the
caller's shorter cancellation deadline), before disconnect, and reports additive
`LogsError` separately from room-close/Analytics outcomes. Reset/shutdown clears
the queue and counters. Module shutdown, Editor assembly reload and leaving Play
mode stop capture synchronously; a worker-initiated stop dispatches native event
unsubscription onto Unity's thread while already refusing further callbacks.
These paths do not wait for network delivery.
Await `ShutdownAsync` / hosted `StopAsync` before an intentional process exit;
Unity does not await its quitting callback. The Dedicated Server sample has an
Inspector opt-in for engine log forwarding.

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
// Subscribe to AdmissionRejected before accepting connections (see admission below).
// room.Admission.TryAdmit(token) includes the only presence join; no HTTP consume.
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
not periodically. New admissions require an open uplink and a valid pushed ticket.
Report removal decisions even while disconnected; reconnect repairs from current
local state without replaying ticket redemption. Frame diagnostics never include raw
frames, credentials or server exception text.

The session token stays in memory and drives companion REST calls. A still-valid
session remains usable through a socket outage. The backend now emits renewal at
half-life; the SDK replaces its in-memory bearer and receipt-clock deadline without
reconnecting or resetting tickets. Missing renewal causes a fresh hello at expiry.
An expired bearer is never sent, and ambiguous REST mutations are not retried.
Terminal authentication/protocol refusal stops automatic reconnect and is exposed
through `Uplink.State`, `Uplink.LastError`, `OnError` and affected room handles.
Failure transitions publish `LastError` together with `Terminated` or `Reconnecting`,
before pending connection attempts complete. A successful handshake clears the
error. Notifications remain dispatched through the configured Unity context;
their event arguments describe the transition even if getters reflect a later state.

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
criterion: PSV-3204 adds the SDK receipt/subscription surface below; deployed-game
acceptance remains separate from these local fixtures.

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

// Ungoverned: send only. Governed: these wrappers await accepted state (not storage durability).
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

`PlayServUplinkDataResult<T>` exposes `Entity`, `Key`, `Found`, `Value`, and optional
`Epoch` / `Revision` on governed connections (the read's revision is a lower bound).
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

No call implicitly connects, queues offline, or falls back to REST mutations.
Ungoverned `Send*Async` completion confirms only transmission; governed wrappers
await acceptance or throw `PlayServUplinkWriteException`, retaining its `Outcome`.
Cancellation after send begins does not roll back a possible write. Credentials and caller-selected
project/environment scope are not added to data frames; scope belongs to the connection.
There is no acting-player overload in this increment. Creating a row in a
player-owned table requires that attribution on the backend and is not supported
by these methods; an ungoverned send may complete even though the backend refuses
the creation. Use existing player-scoped `AsPlayer(...).Records<T>()` for that workflow.

The SDK's local catalogue check is advisory, not a replacement for server enforcement.
Governed state authority enforces its write permissions; ungoverned send-only delivery
does not acquire those guarantees. Keep ETag-aware persistence on the existing Records API.

### Authority receipts and process-owned subscriptions (PSV-3204, 2026-10-23)

Every uplink offers `authoritative_state_v1`. A valid governed acknowledgement binds
the connection to an epoch and a bounded retry session. Only explicit
`enabled:false, reason:not_enabled` selects ungoverned behavior; missing or malformed
metadata fails negotiation, and `authority_unavailable` follows reconnect handling.
Nothing silently falls back to unconfirmed writes on a governed deployment.

```csharp
var outcome = await PlayServGameServer.Uplink.Data.IncrementWithOutcomeAsync(
    "ArenaProgress", progressKey,
    new Dictionary<string, long> { ["completed_matches"] = 1 }, ct: ct);
if (outcome.State == PlayServUplinkWriteState.Unknown ||
    outcome.State == PlayServUplinkWriteState.Pending)
    outcome = await outcome.ResolveAsync(ct); // keep this operation, never issue a fresh increment

using var progress = await PlayServGameServer.Uplink.Data.SubscribeAsync<ArenaProgress>(
    "ArenaProgress", "self", ct);
progress.Changed += change => ApplyProgress(change.Key, change.Deleted, change.Value);
progress.Failed += error => ReportSafeCode(error.SourceCode);
progress.StateChanged += state =>
{
    if (state == PlayServUplinkSubscriptionState.Live && progress.IsComplete)
        ApplyCompleteProgress(progress.Snapshot); // replace membership after epoch recovery
};
if (progress.IsComplete) ApplyCompleteProgress(progress.Snapshot);
```

`ApplyProgress`, `ReportSafeCode`, and `ApplyCompleteProgress` are game-owned callbacks.
Keep one subscription at process scope and dispatch its values to the relevant rooms.
The three `*WithOutcomeAsync` methods return `Accepted`, `Rejected`, `Pending`, `Unknown`,
or explicitly ungoverned `Unconfirmed`. Optional `expectedRevision` guards governed
writes only. Accepted receipts expose authorized server `Data`, `RecordId`, `Deleted`,
`Epoch`, and `Revision`, not a copy of the submitted value. Acceptance is **not storage
durability**. Numeric refusal codes and safe normalized errors remain inspectable;
raw server detail and payload are not diagnostics.

`HttpTimeout` bounds each call, including ACL checks and retry-session refresh. A call
uses at most four attempts and the original session window. Bounded resend and
`ResolveAsync` preserve the operation ID, epoch, retry session and immutable payload;
status `known:false` is not proof a prior attempt failed. Expiry, an epoch change,
disconnect/shutdown or cancellation after a send can leave `Unknown`. Do not replace
that operation with a fresh ID to retry a non-idempotent action.

Subscriptions share one wire route for identical entity/key-path pairs; conflicting
key paths for the same entity are refused locally. Paths are `self`, `field:Name`,
or `relation:Link->Name`; keys remain dataflow keys. The last handle's disposal sends
one unsubscribe; natural reconnect restores surviving routes, while explicit
disconnect, reconfiguration and shutdown retire them. Handles survive natural
reconnect, but newly started operations must reacquire `Uplink.Data`.

Governed subscriptions obtain a paginated full baseline before returning, then use
the saved epoch and safe `Through` checkpoint for reconnect catch-up. A new epoch
replaces membership with a new full baseline. Per-row revisions order live updates,
receipts and pages; deletion tombstones prevent stale resurrection. The highest row
revision is **not** a safe table checkpoint. Own accepted writes are applied before
completion because the platform suppresses own-write pushes; unresolved key mapping
uses bounded catch-up from before the receipt revision, even if live delivery has
advanced the route checkpoint. If local projection or catch-up fails, the receipt
remains Accepted but the subscription reports incomplete/faulted state; use
`RefreshAsync` to recover, not a new write. A relation delete can resolve its key
only while the related target remains available, a platform limitation rather
than a synthesized SDK key.

`State`, `IsComplete`, defensive `Snapshot`, `Changed`, `StateChanged`, `Failed` and
explicit `RefreshAsync` expose recovery. Faulted/incomplete recovery is never marked
complete. Ungoverned handles are `Observed`, containing only delivered rows, not a
table-wide baseline. This is coalesced current state, not a lossless event log.
Callbacks use the captured Unity context outside SDK locks and are fenced after
disposal and epoch/connection replacement. The companion requests row deletes,
not `delete_where` capability.

Limits: 128 correlated pending data calls, 128 subscription routes, 1 MiB per frame,
100,000 retained rows/tombstones and 64 MiB of retained row data per route. Pending
notifications have the same independent per-route row/byte caps and share one
outstanding Unity dispatch; repeated changes to one key coalesce. The latest safe
error survives coalesced recovery within an epoch, while a replacement epoch drops
obsolete callbacks. Exceeding a bound surfaces an error; state is not silently
evicted. Fixture tests exercise production serialization/dispatch with fake transports, not live backend acceptance
or completion of PSV-3078.

## Pushed-ticket admission — required, Unreleased 2026-10-20 (PSV-3181)

Every enabled uplink advertises `admission_push`, independently of room creation,
RPC or operator capabilities. Only `admission: "push"` opens the connection;
missing, null, `consume` or unknown values terminate negotiation with
`uplink_invalid_hello_ack`, without fallback. `Uplink.AdmissionMode` is `Unknown = 0`
before negotiation/reset or the platform-confirmed `Push = 2`.
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
Do **not** also call `ReportJoin`. Negative ack, ack timeout,
ambiguous send or disconnect while ack is pending revoke the local admission.
The SDK updates its roster and signals the game; it cannot disconnect your transport.

Use `room.Admission.ReleaseAsync(token, "room_refused")` for a local refusal before
admission. No automatic send replay is performed. The table expires tickets using
a monotonic clock, clears them with the room and rejects replay. Unused tickets
survive a temporary disconnect until their TTL, but new admission requires a live
uplink. Entry and serialized-byte limits default to 4096 and 4 MiB per process;
configure `AdmissionEntryLimit` / `AdmissionByteLimit` if needed. Limit overflow
refuses new offers rather than evicting a ticket already promised to a player.

**Breaking API removal:** `ConsumeReservationAsync`, `PlayServReservationConsumeResult`,
`EnablePushedAdmission` and `PlayServAdmissionMode.Consume` have been removed.
Replace the old consume-plus-join sequence with `Admission.TryAdmit` and the
mandatory game-owned `AdmissionRejected` reaction shown above. There is no legacy opt-out.
`ReportJoin` remains a no-op for an already-admitted player (and bots), but a new
human player fails locally before roster mutation or an outbound join, including
while disconnected. `EnableUplink = false` retains supported REST-only hosting
operations, not reservation admission. The debug-terminal consume command is
also removed: admission belongs in the game's connection handshake.
This checkout prepares package version `0.8.0`; publication is separate.
Never log reservation tokens, credentials or arbitrary offer parameters.

## Served room routes and reservation lifetime (PSV-2602)

**Unreleased, 2026-09-12:** list uses `GET /rooms/{slug}`, registration and heartbeat
use `POST /rooms/{slug}:upsert`, close uses `POST /rooms/{slug}/{roomName}:close`,
and server/client launch uses `POST /rooms/{slug}/servers:launch`. Managed room
startup and the platform-requested room factory share this registration path.
The algorithmic `matchmaking/{slug}/find` route is unchanged. The companion no
longer calls the retired reservation `:consume` endpoint.
PSV-2600 is implemented in the backend; fixture tests do not prove live integration.

`FindMatchForPlayerAsync` reservations include nullable `Connect`, `Region` and
`Attributes`. Endpoint fields are passed through without DNS or networking setup;
attributes are a snapshot, copied on each access. `ExpiresIn` is nullable integer
seconds from response time. `RemainingLifetime` counts down on a monotonic clock,
floored at zero; missing/null TTL means unknown on older backends, not unlimited.
Do not compare the retained `ExpiresAt` timestamp with a local wall clock. Invalid
TTL types or ranges return a typed `InvalidResponse`. Admission remains authoritative.

Pushed admission retains typed refusal reasons such as `room_closed`,
`reservation_expired`, `reservation_consumed`, `reservation_invalid`,
`room_mismatch` and `room_refused`; malformed wire reasons are rejected.
HTTP failures for supported operations still throw
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

`PlayServGameServer.Records<T>().SubscribeManagedAsync(query, ct)` opens a live
collection of stable, saveable record handles after connecting Records realtime.
It shares the core [managed collection behavior](../../Documentation~/managed-collections.md),
including server credentials, conflicts and membership/disposal rules.

Retained server records support `record.ObserveField(...)` and `record.Changed`
for reload and live updates, using the core
[managed observer contract](../../Documentation~/managed-record-observers.md).

A single server record's `SubscribeAsync()` also uses the core direct-push path:
complete versioned overwrites update state and `If-Match` without an extra GET;
incomplete frames and reconnect/reset recovery still read canonical state.

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

Declared parts and relations use the core split-save implementation, including
partial-failure recovery handles and `ReconcileAsync`. Parent patches use ETags;
nested PUT routes do not enforce `If-Match`. See
[saving parts and relations](../../Documentation~/split-saves.md) for required
schema setup, accepted partial writes and retry behavior.

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

## Operator room actions (PSV-2884)

Operator actions are opt-in, independently of `RoomFactory` and pushed admission.
Configure `ParticipantRemoveHandler` and/or `RoomCloseHandler` before connecting;
each non-null handler advertises its corresponding capability. Configuration
captures the delegates. No handler means the platform reports `not_supported`.

```csharp
PlayServGameServer.Configure(new PlayServGameServerOptions
{
    ExecutorSlug = "arena",
    ParticipantRemoveHandler = gameNetworking.RemoveParticipantAsync,
    RoomCloseHandler = gameWorld.EndRoomAsync
});
```

These game-owned methods accept `PlayServParticipantRemoveContext` or
`PlayServOperatorRoomCloseContext` plus a `CancellationToken`, and return
`Task<bool>`. They must actually disconnect all targeted game connections/end
the simulation before returning `true`; an already absent target also succeeds.
They are called even when the SDK has no managed handle for the target. Never
use an always-true placeholder or return immediately after merely scheduling
a networking disconnect. Both dedicated-server samples expose delegates for
real game cleanup and advertise nothing until those delegates are connected.

The immutable contexts expose `RequestId`, `RoomName`, and `Reason`;
participant removal additionally exposes `PlayerId`. Reasons are
`removed_by_operator` and `room_closed_by_operator`. Handlers run through the
configured Unity context, outside locks and the receive loop. The SDK echoes
the exact `request_id` in `participant_remove_result` / `room_close_result`.
`false` or an exception produces only safe `operator_action_failed`, never an
exception message or gameplay payload.

On removal the SDK immediately revokes that generation's tickets/pending joins,
removes the player from its tracker/roster without reconnect grace, and queues
one leave for a present player. The new handler owns physical disconnection;
the SDK suppresses duplicate tracker/admission disconnect notifications for
that operator target. Late acknowledgements/offers cannot re-admit the player.
On close it stops admissions, heartbeats and timers, cancels pending room
preparation/registration and closes locally without another REST `:close`.
Neither failed nor timed-out game cleanup restarts the room.

The total budget is five seconds from frame receipt, including Unity dispatch
waiting. The serial queue is bounded to 64 active/queued actions and 1 MiB of
input; overload gives no confirmation. Duplicate active IDs do not run twice.
Timeout, connection loss, reconfiguration and shutdown cancel the local token;
there is no late reply on a replacement connection and no automatic replay.
A callback ignoring cancellation keeps its serial slot and name reservation
until it actually settles, while shutdown does not wait indefinitely for it.

An operator close is **not** idle/lifetime expiry. Do not automatically reopen
a fixed-name room in response; decide explicitly whether to stop that world or
end the process. A deliberate new generation is allowed after the action
settles. `RoomCreationCompleted` exposes `outcome.Request`, the exact immutable
context supplied to the factory (null if it was not invoked). Match it by
reference to your captured preparation, not only room name, before asynchronous
cleanup. Manual `Kick`, `CloseAsync` and supported REST-only hosting operations
retain their existing behavior.

Coverage here uses fake HTTP/WebSockets with the real serializer/dispatcher.
Live acceptance still requires both admin actions on deployed dev to report
`confirmed` and actually remove the player/end the integrated game's room.

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
it to `room.Admission.TryAdmit` on an open, push-negotiated uplink. Subscribe to
`AdmissionRejected` before accepting game connections. The package deliberately has no dependency on
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
Runtime schema presence can be checked explicitly with `PlayServGameServer.CheckSchemaAsync(types, ct)`.
The result also compares singleton/table kind for attributed entity contracts;
missing kind metadata stays unknown. A table missing from the caller-visible
catalogue may be ACL-hidden. This does not validate field types or modify schema.
The core SDK's automatic opt-in advisory runs on player authentication; dedicated
servers keep using this explicit API with server credentials.
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
