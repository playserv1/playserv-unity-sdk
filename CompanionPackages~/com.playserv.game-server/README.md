# PlayServ Game Server

`com.playserv.game-server` is the server-credential companion for Unity
Dedicated Server builds. It exposes server matchmaking, room registration and
heartbeats, reservation admission, server launch, room inspection, and a safe
player-profile lookup over the existing PlayServ runtime API. It also exposes
typed Records and Cloud Functions using the same rotating server credential.
Server Analytics, Catalog, and Storefront reads use that credential as well.

## Install

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

For secret rotation, provide a resolver. It runs for every HTTP request:

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
log, or source-controlled config. The package sends only
`Authorization: Bearer sk_*`; it never adds a public client token, player JWT,
or environment header.

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
it to `ConsumeReservationAsync`. The package deliberately has no dependency on
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
