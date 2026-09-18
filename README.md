# PlayServ Unity SDK

PlayServ is a modular multiplayer SDK for Unity. The core package provides
runtime connection management, data subscriptions, typed events, RPC, HTTP cloud functions,
read-only catalog/storefront and public platform-status access, spawning,
and the built-in HTTP/WebSocket transport. Analytics, identity providers,
native transports, WebRTC, Pulse, schema tooling, and Dedicated Server runtime
operations are optional companion packages.

## Install with OpenUPM (recommended for Package Manager updates)

See [Rooms runtime integration](Documentation~/rooms-runtime-parity.md) for admission parameters and dedicated-server integration helpers.

The core package is available on [OpenUPM](https://openupm.com/packages/com.playserv.sdk/).
Install it from the registry to browse and select published versions in Unity
Package Manager. No GitHub credentials or SSH key are needed for this method.

This checkout prepares **0.6.1**. The examples below target that version, but do
not mean it has been published to OpenUPM or GitHub yet. Until the distribution
tag and OpenUPM build are published, select an already published version or use
this checkout as a local package. Version preparation does not publish a release.

1. Open `Edit` -> `Project Settings` -> `Package Manager`.
2. Add a scoped registry, or add the scope to your existing OpenUPM entry:

   - **Name:** `OpenUPM`
   - **URL:** `https://package.openupm.com`
   - **Scope:** `com.playserv.sdk`

3. Click `Save` / `Apply`.
4. Open `Window` -> `Package Manager`, choose `+` -> `Add package by name`
   (`Install package by name` in some Unity versions), enter `com.playserv.sdk`
   and version `0.6.1`, then install. You can also find it under `My Registries`.

Alternatively, merge this configuration into the game project's
`Packages/manifest.json`. Keep existing dependencies and scoped registries; do
not replace the whole file:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": ["com.playserv.sdk"]
    }
  ],
  "dependencies": {
    "com.playserv.sdk": "0.6.1"
  }
}
```

### Switch an existing Git installation to OpenUPM

Adding the registry alone does not convert an existing Git dependency. After
adding the registry above, replace the **existing** `com.playserv.sdk` Git URL
in `Packages/manifest.json` with the version string `"0.6.1"`; do not add a
second entry. A local or embedded copy must also be removed from the resolution
path before the registry package can be used (back up any local edits first).
Let Unity resolve packages, then commit `Packages/manifest.json` and
`Packages/packages-lock.json`. Do not manually edit or delete the lock file.

### Update an OpenUPM installation

New releases become selectable after OpenUPM has successfully built and
published the corresponding distribution Git tag. A source commit or an
unprocessed tag is not yet a registry release; check the package's OpenUPM page.

Refresh Package Manager, select **PlayServ SDK** under `In Project` or
`My Registries`, then select the desired published version and click `Update` /
`Install`. Depending on the Unity version, use the version dropdown or
`Version History`. You can also use `Add package by name` with an explicit
version, or change the version string in the manifest. Keep the manifest and
lock file in version control after the update.

**Updates are available through Package Manager, not silently installed.** The
project stays on its selected version until you choose another. Git-tag installs
remain pinned to their tag and do not gain registry version selection just
because OpenUPM is configured. Unity requires an exact version here, not
`latest`, `^0.6.1`, or `*`.

The scope above covers only the core package. It does not publish or migrate
the optional companion packages. Until a companion has its own OpenUPM listing,
install it with the Git URL below and keep its tag aligned with the core version;
updating core alone does not update Git-installed companions. The PlayServ
module-settings installer still uses Git URLs for companions.

See the [OpenUPM setup guide](https://openupm.com/docs/getting-started.html) and
[Unity's package update guide](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-update.html).

## Install from GitHub (alternative)

This checkout targets SDK `0.6.1`. In Unity, open `Window` ->
`Package Manager`, choose `Add package from git URL`, and paste this pinned core
package URL:

```text
git@github.com:playserv1/playserv-unity-sdk.git#0.6.1
```

The Git URLs below require the `0.6.1` distribution tag to be published first;
changing the version in the source repository does not publish that tag.

The distribution repository is public. The SSH URLs shown here still require
GitHub SSH authentication on the operating-system account running Unity; verify
it with `ssh -T git@github.com`. To clone without SSH credentials, use
`https://github.com/playserv1/playserv-unity-sdk.git#0.6.1` instead. The same
HTTPS substitution works for companion URLs, retaining `?path=...#<version>`.
Unity uses the machine's Git credentials; PlayServ does not read or store them.

The same dependency can be added directly to the game project's
`Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#0.6.1"
  }
}
```

### Select, upgrade, or downgrade a version

Every published SDK version is an immutable plain-SemVer tag in the
[`playserv-unity-sdk` tag list](https://github.com/playserv1/playserv-unity-sdk/tags),
for example `0.4.0`, `0.4.1`, or `0.5.1`. To select another release, replace the
suffix in every PlayServ Git dependency with `#<version>`:

```text
git@github.com:playserv1/playserv-unity-sdk.git#<version>
```

The distribution `main` branch points to the newest published snapshot, but
production projects should always include `#<version>` so an install cannot
change unexpectedly. After changing versions, let Unity update the dependency
graph, then commit both `Packages/manifest.json` and `Packages/packages-lock.json`.

Published tags are never moved, overwritten, or deleted. Historical releases
remain available through their tags; separate version folders and version
branches are not used.

## Requirements

- Unity 2021.3 or newer.
- A PlayServ public runtime client token (`pk_*`).
- `com.unity.nuget.newtonsoft-json` 3.2.2. Unity Package Manager installs this
  dependency automatically for UPM installations.

## Companion packages

Optional PlayServ features are distributed as separate packages. With the Git
repository, install core first and add only the required packages:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#0.6.1",
    "com.playserv.apple-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.apple-signin#0.6.1",
    "com.playserv.google-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.google-signin#0.6.1",
    "com.playserv.facebook-login": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.facebook-login#0.6.1",
    "com.playserv.epic-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.epic-auth#0.6.1",
    "com.playserv.steam-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.steam-auth#0.6.1",
    "com.playserv.webrtc": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.webrtc#0.6.1",
    "com.playserv.analytics": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.analytics#0.6.1",
    "com.playserv.pulse": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.pulse#0.6.1",
    "com.playserv.debug-terminal": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.debug-terminal#0.6.1",
    "com.playserv.transports-native": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.transports-native#0.6.1",
    "com.playserv.game-server": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.game-server#0.6.1",
    "com.playserv.schema-tool": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#0.6.1"
  }
}
```

Only add the companion packages used by the game. Unity package dependencies
cannot contain Git URLs, so private Git installations must declare core and
each companion package directly in the project manifest. Use the same immutable
version tag for core and every companion package. When adding a companion
through `Add package from git URL`, paste its complete `?path=...#<version>` URL.

Runtime companion packages can also be managed from `Tools` -> `PlayServ` ->
`Settings` -> `SDK module settings`. They stay visible there even when they are
not installed. `Install` and `Remove Package` update the project through Unity
Package Manager. `Installed` and `Enabled` are separate states: disabling an
installed module excludes its assembly through a scripting define without
removing the package. The Native Transports package exposes separate toggles for
UDP and RUDP.

The `Schema Workflows` section in the main PlayServ window exposes both schema
directions. `Local contracts` installs the external UPM Schema Tool to analyze
`[PlayServSchema]` C# contracts, generate JSON Schema and backend DTOs, validate
drift in CI, and watch project files. `Server schema` downloads the selected
project schema from the PlayServ Schema API, previews its difference from the
accepted schema, and generates Unity C# models only after explicit
confirmation.

## Cache maintenance

When the installed SDK version or PlayServ cache schema changes, the editor
automatically clears PlayServ generated state, the shared codegen cache, and
inactive `Library/PackageCache/com.playserv.*` directories. Active package
caches and unrelated Unity caches are preserved.

Use `Tools` -> `PlayServ` -> `Cache` -> `Clear PlayServ Cache` for a manual
targeted cleanup. `Rebuild Project Library...` is the recovery option for a
corrupted Unity cache: after confirmation it closes Unity, deletes the complete
project `Library` directory, and reopens the project.

## Configure

Optional player names are explicit: set runtime `PlayServSettings.AnonymousDisplayName`
for new anonymous players, or use `proof.WithDisplayName(chosenName)` for external
login/link. Names are trimmed and capped at 64 UTF-16 code units, not a rename API.
See [Player authentication](Documentation~/playserv-sdk.md#player-authentication).

Open `Tools` -> `PlayServ` -> `Settings`, select an environment and configure the
public client token, game version, and backend endpoint. The token selects the
project; automatic authentication creates or restores the player session.

Dev and Prod client tokens are independent, local Editor settings, scoped to this
Unity project and config asset. Switching to an unconfigured environment leaves
the token empty. The Settings window and config Inspector edit the same local
store; changing a token does not reconnect an already configured SDK session.
Existing serialized tokens are migrated once to the selected environment and
removed from the config asset, without copying them to the other environment.

Player builds temporarily bake only the active public `pk_*` into the config and
restore the asset afterwards. The key is public and remains readable in the
player build; local storage is not encryption. For headless builds, supply both
`PLAYSERV_ENVIRONMENT=Prod` and `PLAYSERV_CLIENT_TOKEN` through the build process
environment. These overrides are not saved locally. Configure build endpoints
explicitly as before; these variables do not rewrite endpoint overrides.
See [Environment client tokens](Documentation~/playserv-sdk.md#environment-client-tokens)
for migration and interrupted-build recovery.

```csharp
using Playserv.Wrapper;

PlayServ.Config(new PlayServSettings
{
    ClientToken = "pk_...",
    GameVersion = "1.0.0",
    BackendServerAddress = "wss://your-playserv-endpoint/ws"
});

bool connected = await PlayServ.Connect();
```

### Migrating legacy connection identity

`PlayServSettings.GameId`, `PlayServSettings.UserId`, and the matching
positional `Config` arguments are no longer part of the runtime API. Remove
them from bootstrap code: the `pk_*` client token selects the project and the
authenticated JWT selects the player. Read the verified player identifier from
`PlayServAuth.PlayerId` or `PlayServAuth.CurrentSession` after authentication.
If Editor deployment/version tools still need the old deployment identifier,
move only that value to `PlayServSettings.DeploymentGameId`; it is never sent
over the runtime WebSocket.

With a public `pk_*` client token and no custom runtime token provider,
`Connect()` automatically creates or restores an anonymous player. Promote that
same player after a provider SDK returns an ID token:

```csharp
using Playserv.Identity;
using Playserv.Wrapper;

var proof = PlayServExternalIdentityProof.FromGoogleIdToken(
    googleIdToken,
    expectedNonce);

var login = await PlayServAuth.LoginExternalAsync(proof);
if (login.IsSuccess)
    UnityEngine.Debug.Log($"Registered player: {login.Session.PlayerId}");
else if (login.Conflict != null)
    UnityEngine.Debug.LogWarning("The provider belongs to another player.");
```

`PlayServAuth.GetProvidersAsync()` exposes the providers enabled for the current
project/environment. `LinkIdentityAsync`, `UnlinkIdentityAsync`, and
conflict-driven `MergeIdentityAsync` manage additional identities without
requiring game code to construct arbitrary player-ID pairs. Linked providers
from the current managed JWT are available through
`PlayServAuth.CurrentSession.LinkedProviders`.

After sign-in, `await PlayServAuth.GetCurrentPlayerProfileAsync()` reads the
caller's safe runtime profile (display name, status, linked providers and public
timestamps). Repeated calls use the cached profile; use
`RefreshCurrentPlayerProfileAsync()` when fresh backend state is required. The
runtime projection never exposes moderation details, IP addresses or fingerprint
hashes.

Use `PlayServExternalLoginMode.RecoverProviderAccount` on a new installation to
recover the provider-owned account instead of preserving the current anonymous
player. `PlayServAuth.LogoutAsync()` revokes the managed session when possible,
clears it locally, creates a fresh anonymous player, and reconnects an online
transport. Subscribe to `PlayServAuth.SessionLost` for revoked, merged, banned,
expired, or environment-mismatched sessions.

For authenticated players, provide the JWT only at runtime:

```csharp
using Playserv.Runtime.Abstractions;

PlayServ.SetRuntimeTokenProvider(
    new PlayServDelegateRuntimeTokenProvider(ct => sessionService.GetPlayServJwtAsync(ct)));
```

Never put `sk_*` keys or player JWTs in `PlayServConfig`. Deployment credentials
are read by Editor tools from `PLAYSERV_DEPLOY_AUTH_TOKEN` or project-scoped local
Editor storage.

Unity Dedicated Server builds that need `sk_*` runtime operations should install
`com.playserv.game-server`. Its separate `PlayServGameServer` facade reads
`PLAYSERV_SERVER_KEY` per request (or uses a rotating key provider), manages
multi-room heartbeats and exposes server matchmaking, reservation admission,
room lifecycle, safe player lookup, server-authorized typed Records, Cloud
Functions, bounded Analytics, Catalog and Storefront reads. The package rejects
operations in normal client builds before
credential resolution or HTTP. See the
[Game Server README](CompanionPackages~/com.playserv.game-server/README.md).

## Typed records

Resolve a schema table from the CLR type name, or pass its explicit `ent_*` ID,
then keep the returned handle for ETag-safe updates:

```csharp
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.Wrapper;

var items = PlayServData.Records<InventoryItem>();
var loaded = await items.LoadOrCreateAsync(
    PlayServNaturalKey<InventoryItem>.For(x => x.Code, "starter-sword"),
    () => new InventoryItem { Code = "starter-sword", Durability = 100 });

loaded.Record.Value.Durability--;
await loaded.Record.SaveAsync();
```

`CreateAsync` always accepts the backend's server-minted `rec_*` ID. Handles
retain the canonical snapshot and ETag, send only a top-level JSON Merge Patch,
and raise `PlayServRecordConflictException` for structured `409`/`412` conflicts.
Projection loads and queries with hidden columns are partial and must call
`ReloadAsync()` before saving.

For client-side bulk work, `LoadAllAsync` walks cursors up to an explicit
record limit, while `LoadManyAsync`, `BulkSaveAsync`, and `BulkDeleteAsync` use
bounded parallel requests and return an ordered result for every item.
`DeleteByIdAsync` avoids creating a handle, and `PopulateAsync` /
`PopulateManyAsync` load relation target IDs through the target record set.
These operations use existing point and query routes and are not atomic.
`DeleteAllAsync` enumerates the complete set before deleting anything, refuses
to continue when `maxRecords` truncates the load, and requires
`AllRecords` confirmation for an unrestricted query.

A full record handle can stay current through the existing realtime transport:

```csharp
IPlayServRecordSubscription<InventoryItem> recordLive =
    await loaded.Record.SubscribeAsync();
recordLive.Changed += change =>
    UnityEngine.Debug.Log(string.Join(", ", change.ChangedFields));
recordLive.Conflict += conflict =>
    ShowUnsavedValueWasReplaced(conflict.LocalValue, conflict.RemoteValue);

// Request the current backend snapshot and wait for Value/UpdatedAt/ETag sync.
await recordLive.RefreshAsync(cancellationToken);
```

Realtime pushes trigger a canonical record GET so `Value`, its snapshot,
`UpdatedAt`, and `ETag` advance atomically. Unsaved edits made while subscribed
use an explicit backend-wins policy: `Conflict` is raised before `Changed` and
includes both values and their changed top-level wire fields. Server deletion
sets `IsDeleted`, retains the last value, and terminates the handle. Record
handles use the same refcounted close and reconnect/replay lifecycle as other
managed subscriptions.

`await items.GetCapabilitiesAsync()` exposes the table catalogue's client,
server, and backend ACL flags. Unity uses the client flags for advisory read and
write prechecks; `RefreshCapabilitiesAsync()` reloads them after a schema
change. Missing ACL metadata stays `Unknown` and does not block requests, while
the backend remains authoritative for player ownership and row-level access.

Use `PlayServData.GetTablesAsync()`, `GetTableAsync(idOrName)`, and
`RefreshTablesAsync()` to inspect the same cached metadata directly, including
description, singleton status, row count, timestamp, read policy, and ACLs.

The same typed query object drives REST reads and realtime collections:

```csharp
var query = new PlayServRecordQuery<InventoryItem>()
    .Where(x => x.Durability >= 10)
    .SelectFields(x => x.Code, x => x.Durability)
    .WithLimit(50);

PlayServRecordPage<InventoryItem> page = await items.QueryAsync(query);
ISharedCollection<InventoryItem> live = await items.SubscribeAsync(query);
live.Failure += error => UnityEngine.Debug.LogError(error.ToString());

// Useful after returning from background or when an explicit resync is needed.
await live.RefreshAsync(cancellationToken);

// Deterministic server unsubscribe; Dispose() is the best-effort alternative.
PlayServSubscriptionCloseResult closed = await live.CloseAsync();
```

REST supports the complete records grammar. Realtime additionally supports OR
groups and relation expansion up to six levels:

```csharp
var regional = new PlayServRecordQuery<InventoryItem>()
    .Where(x => x.Durability > 0)
    .Or(x => x.Region == "eu", x => x.Region == "us")
    .Include(x => x.Guild.Owner);

ISharedCollection<InventoryItem> liveRegional =
    await items.SubscribeAsync(regional);
```

Each `Or` expression is an AND group and ordinary `Where` clauses apply to all
alternatives. OR and nested `Include` are realtime-only; passing either to
`QueryAsync` raises `PlayServQueryCapabilityException` before HTTP I/O. Realtime
natively supports `Eq/Neq`, comparisons, string operators, limit and projection;
`In`, null/empty/count checks, search, sort, cursor, and `Hide` raise
`PlayServQueryCapabilityException` before the transport request. REST
`SelectFields`/`Include` uses bounded per-record GET hydration because the
existing `records:query` endpoint does not accept `fields` or `expand`.

Identical realtime queries share one server subscription until the final handle
closes. Active handles are rebound after reconnect, and server termination or
record deletion moves them to `PlayServSubscriptionState.Terminated` without
replaying them. All SDK error surfaces expose a common `PlayServError` through
`UnifiedError`; existing auth, RPC, transport, records, and subscription error
types remain supported.

Built-in desktop and WebGL WebSocket transports enforce the backend's 1 MiB
message limit in both directions. Oversized outbound messages fail before I/O;
oversized inbound messages terminate the connection with close code `1009` and
surface `PlayServWebSocketPayloadException` without retaining the payload.

Collection, typed-record, and legacy shared-entity handles implement
`IPlayServRefreshableSubscription`. `RefreshAsync(ct)` waits for the correlated
server snapshot to be applied without opening or closing the subscription. A
canceled wait leaves the handle active; a later server frame may still update it.

Optional modules are enabled per project and stored in
`ProjectSettings/PlayServModules.json`.

## Player matchmaking

Use request objects when placement needs typed waiting-room state or a custom
long-poll duration. The SDK snapshots `Parameters` once and sends it as the
backend's `params` object on every poll:

```csharp
using Playserv.Matchmaking;
using Playserv.Wrapper;

PlayServJoinGameResult join = await PlayServMatchmaking.JoinGameAsync(
    new PlayServJoinGameRequest
    {
        FunctionSlug = "tank-room",
        Matchmaker = "ranked",
        Parameters = new { mode = "duo", skill = 1700 },
        WaitMs = 20_000
    },
    cancellationToken: destroyCancellationToken);
```

`FindMatchAsync(PlayServFindMatchRequest, ...)` additionally exposes explicit
`SearchAgeMs`. Existing positional Find/Join overloads remain available.
Games can request an orchestrated server through
`LaunchServerAsync(functionSlug, region)` via `POST /rooms/{slug}/servers:launch`. A successful result means the
deployment was accepted; the room becomes available only after the server
self-registers.

Find, Join, and Launch operational failures use
`PlayServMatchmakingException`, which includes the operation, function slug,
and normalized `UnifiedError`. Caller cancellation and invalid API arguments
keep their existing exception behavior.

### Host a room

`HostRoomAsync` (PSV-2694) asks an already connected server to create a room through
`POST /rooms/{slug}:host`. It requires the configured client key and a signed-in
player JWT, just like Join. It does not launch a server or connect a game transport.

```csharp
PlayServMatchResult hosted = await PlayServMatchmaking.HostRoomAsync(
    new PlayServHostRoomRequest
    {
        FunctionSlug = "tank-room",
        Attributes = new { title = "Friends", max_players = 8, bots = 2 }
    },
    new PlayServRoomHostOptions { Timeout = System.TimeSpan.FromSeconds(45) },
    ct: destroyCancellationToken);

var reservation = hosted.Reservation;
// Share reservation.RoomName as the invite code, not the display title.
// Render reservation.Attributes: the server may change or refuse the requested values.
// Pass Connect and ReservationToken to your own game networking/admission code.
// Connect can be null; Host does not poll, join again, or dial an endpoint.
```

Attributes are an optional flat JSON object of at most 2048 UTF-8 bytes; nested
objects and arrays are rejected locally. They are a **wish**, not trusted room
configuration. The platform mints the room name; the request has no `RoomName`.
Optional `Region` is passed to the platform. Request/options are snapshotted
before authentication can yield; response attributes are immutable snapshots.

The configurable 45-second default budget includes token resolution and HTTP.
There is exactly one Host request: no retry, placement, launch or join fallback.
A timeout/cancellation after sending does **not** prove that no room was created;
repeating Host may create another room. A successful reservation already belongs
to the hosting player, so do not perform a separate Join just to enter it.

Catch `PlayServMatchmakingException` and distinguish `RoomQuotaExceeded`,
`RoomHostUnavailable`, `RoomRefused` (safe reason in `UnifiedError.Message`) and
`RoomUnreachable` (`RetryAfter` when supplied). The current backend can also return
`RoomHostCapacityExhausted` and `RegionUnavailable`. `UnifiedError.SourceCode`
preserves unknown codes. No refusal triggers automatic retry. An unavailable host
means there is nowhere to play now, not that the client should launch infrastructure.
Some placement refusals are aggregated by the backend into `room_host_unavailable`;
the SDK preserves that response and cannot recover a discarded content-refusal detail.

Backend Host is implemented by PSV-2626. SDK tests use HTTP fixtures, not a live
multi-player or deployed-server integration check.

### Room browser and direct join

`BrowseRoomsAsync` and `JoinRoomAsync` use the `/rooms` namespace, implemented in
the backend by **PSV-2600**. Unity HTTP fixtures verify the served contract but
do not establish deployed end-to-end integration. There is
no fallback to the deprecated matchmaking routes or to `find { room_name }`.

```csharp
var page = await PlayServMatchmaking.BrowseRoomsAsync("tank-room",
    new PlayServRoomBrowseQuery
    {
        PlacementState = "open",
        Region = "eu-west",
        Attributes = new System.Collections.Generic.Dictionary<string, string>
        {
            ["map"] = "arena"
        },
        Limit = 50
    }, ct: destroyCancellationToken);

// Render page.Rooms in your server browser; this example selects its first row.
if (page.Rooms.Count > 0)
{
    var chosenRoom = page.Rooms[0];
    PlayServMatchResult selected = await PlayServMatchmaking.JoinRoomAsync(
        "tank-room", chosenRoom.RoomName, ct: destroyCancellationToken);
    PlayServRoomConnect endpoint = selected.Reservation.Connect;
    System.TimeSpan? remaining = selected.Reservation.RemainingLifetime;
    // Hand endpoint and ReservationToken to your own admission/networking code.
    // Never log the reservation token. The SDK does not dial or interpret endpoint.
}
// When page.HasMore, request the next page with Cursor = page.CursorNext,
// retaining the same filters and limit.
```

Browse defaults to 50 rows (range 1–200), accepts at most four string-form
`attributes.<key>=<value>` equality filters, and preserves server ordering. Rows
contain only client metadata; no drain or instance internals. Unfiltered results
can include `session_closing` rooms (visible but unjoinable), reservation-only
rooms with null `Connect`, and `Players > Capacity` after a capacity reduction.
A listing is not a promise of admission: the server decides when JoinRoom runs.

Direct join performs one bodyless POST, without placement, launch, polling or
automatic retry. It returns the same result type as `FindMatchAsync`; only a
matched reservation is valid. Catch `PlayServMatchmakingException` and switch on
`RoomFailureCode` (`RoomNotFound`, `RoomFull`, `RoomClosed`, `RoomTypeNotFound`,
`RoomRefused`, `RoomUnreachable`, or `Unknown`). `UnifiedError.SourceCode`
preserves the backend code; its `Message` contains the credential-filtered
Problem Details reason, including `draining` or a room's refusal detail.
`RoomRefused` and retryable `RoomUnreachable` are prepared for **PSV-2601**.

Find and Join reservations additively expose `Connect`, `Region`, `Attributes`
and `ExpiresIn`. `Connect` is nullable and its host, port, transport and optional
connect string are passed through verbatim. `RemainingLifetime` counts down from
`ExpiresIn` on a monotonic clock, never by comparing `ExpiresAt` with the device
clock. It floors at zero; null means unknown on older FindMatch responses, not
an unlimited lifetime. Server admission remains authoritative. Existing Find
outcomes and `JoinGameAsync` re-entry on `room_closed` are unchanged.

The player facade still excludes server room heartbeat, close, administrative
listing and reservation consumption. Use `com.playserv.game-server` for those
server-authorized operations. **Unreleased, 2026-09-12 (PSV-2602):** client/server
launch and companion list, registration/heartbeat and close use `/rooms/...`.
Algorithmic `find` and reservation `consume` stay on `/matchmaking/...`; no
deprecated-route fallback is added. Server reservations now expose the same
endpoint metadata and monotonic lifetime semantics as client reservations.

## Cloud functions

Call deployed `cloud_function` workloads through the existing `/fn/{slug}`
gateway. The SDK automatically supplies the configured public client token and,
when available, the current managed or custom player JWT:

```csharp
using Playserv.Code;
using Playserv.Wrapper;

PlayServFunctionResult<RewardResponse> result =
    await PlayServCode.CallAsync<RewardResponse>(
        "grant-daily-reward",
        new { streak = 7 },
        new PlayServFunctionCallOptions { Version = "v2" });

if (!result.IsSuccess)
    UnityEngine.Debug.LogError(result.Error);
```

`InvokeAsync` additionally supports GET, POST, PUT, PATCH and DELETE, query
parameters, raw bodies, pass-through non-credential headers, response metadata,
per-request timeouts and plain-text responses. Credential headers and all
`X-Playserv-*` headers are reserved; select a tagged deployment through the
dedicated `Version` property.

Binary payloads use the exact-byte API:

```csharp
var generated = await PlayServCode.InvokeBytesAsync(
    new PlayServFunctionRequest
    {
        Slug = "generate-map",
        RawBodyBytes = sourceBytes,
        ContentType = "application/octet-stream"
    },
    new PlayServFunctionTransferOptions
    {
        MaxResponseBytes = 16 * 1024 * 1024,
        Progress = transferProgress
    },
    cancellationToken);

byte[] mapBytes = generated.Response.BodyBytes;
```

Use `DownloadToFileAsync` for larger responses. It streams into a same-directory
temporary file and exposes a 512 MiB default limit. Existing targets are refused
unless `OverwriteExistingFile` is enabled, and a failed or cancelled transfer
does not replace the target. File download is unavailable on WebGL; buffered
binary invocation remains supported there.

## Catalog and storefronts

Read the published runtime catalog and player-facing storefront configuration
through the existing HTTP endpoints. Both facades use the public `pk_*` token
and include the current player JWT when one is available:

```csharp
using Playserv.Commerce;
using Playserv.Wrapper;

PlayServStorefrontPage page = await PlayServStorefronts.ListAsync(
    new PlayServStorefrontQuery { Status = "live", Limit = 25 });

foreach (PlayServStorefront storefront in page.Storefronts)
{
    foreach (PlayServStorefrontItem entry in storefront.Items)
    {
        PlayServCatalogItem item = await PlayServCatalog.GetAsync(entry.ItemId);
        UnityEngine.Debug.Log($"{storefront.Name}: {item.Name}");
    }
}
```

`ListAsync` supports the backend's status, search, sort, cursor, limit, and
storefront audience filters. Responses expose typed nested catalog mappings,
localizations, storefront audiences, schedules, statistics, and bidirectional
page cursors. These runtime APIs are intentionally read-only; purchases and
receipt validation are not exposed by the current backend endpoints.

## Public platform status

Read current health, daily availability, and the cluster's advertised status
peers without a public key or player session:

```csharp
using Playserv.Status;
using Playserv.Wrapper;

PlayServPlatformStatus current = await PlayServStatus.GetCurrentAsync();
PlayServPlatformStatusHistory history =
    await PlayServStatus.GetHistoryAsync(pop: "iad-1", days: 30);
PlayServStatusFederation peers = await PlayServStatus.GetFederationAsync();
```

The SDK validates federation entries as absolute HTTP(S) origins but never
contacts them automatically. Your game remains responsible for deciding which
cross-origin status hosts it trusts. Status transport and response failures use
`PlayServStatusException.UnifiedError`; cancellation remains an
`OperationCanceledException`.

## Analytics

Install `com.playserv.analytics`, enable `Analytics` in `Tools` -> `PlayServ` ->
`Settings` -> `SDK module settings`, connect PlayServ, and track explicit
gameplay events:

```csharp
using System.Collections.Generic;
using Playserv.Wrapper;

PlayServAnalytics.SetUserProperty("role", "parent");
PlayServAnalytics.Track(
    "login_sso_success",
    new Dictionary<string, object>
    {
        { "provider", "google" },
        { "attempt", 1 },
        { "new_user", true }
    });
```

Events are queued in memory, enriched with PlayServ user/session/app context,
and sent in bounded batches to the existing `POST /analytics/events` runtime
endpoint. Applications can install a custom `IPlayServAnalyticsProvider` when
events must also be delivered to another analytics service.

## Samples

Select PlayServ SDK in Package Manager and import **PlayServ SDK Examples**.
The core sample contains data subscription, events, RPC, and spawn scenes
without compiling example code into projects that do not import it. Install
`com.playserv.debug-terminal`, enable `Debug Terminal`, and import its **Debug
Terminal** sample for the authenticated connection popup and command console.
The terminal now exercises Analytics, Cloud Functions, Catalog/Storefront,
player matchmaking, Data ACL capabilities, and realtime `PlayServRecord<T>`
handles in addition to the legacy module smoke tests. It depends on the matching
`com.playserv.analytics` package and deliberately redacts matchmaking
reservation tokens and function response headers.

## Documentation

- [Complete Unity SDK guide](Documentation~/playserv-sdk.md)
- [Player authentication](Documentation~/playserv-sdk.md#player-authentication)
- [Typed V2 records](Documentation~/playserv-sdk.md#typed-v2-records)
- [Player matchmaking](Documentation~/playserv-sdk.md#player-matchmaking)
- [Cloud functions](Documentation~/playserv-sdk.md#cloud-functions)
- [Catalog and storefronts](Documentation~/playserv-sdk.md#catalog-and-storefronts)
- [Server/shared runtime guide](Documentation~/server-sdk.md)
- [Analytics setup](Documentation~/playserv-sdk.md#analytics-module)
- [External Schema Tool](Documentation~/playserv-sdk.md#external-schema-tool)
- [Apple Sign In setup](Documentation~/playserv-sdk.md#apple-sign-in-module)
- [Google Sign In setup](Documentation~/playserv-sdk.md#google-sign-in-module)
- [Steam Auth setup](Documentation~/playserv-sdk.md#steam-auth-module)
- [Epic Auth setup](Documentation~/playserv-sdk.md#epic-auth-module)
- [Facebook Limited Login setup](Documentation~/playserv-sdk.md#facebook-limited-login-module)
- [Changelog](CHANGELOG.md)

## Development

The SDK source of truth is `playserv-platform/unity/com.playserv.sdk`. The
private `playserv1/playserv-unity-sdk` repository is generated distribution
output for studio UPM installs. SDK changes and releases must be made in the
platform repository, not committed directly to the distribution repository.

## Tests

Package tests live under `Tests/Editor` and `Tests/Runtime`. Add
`"com.playserv.sdk"` to the consuming project's `testables` array before running
them with Unity Test Framework. The complete guide contains batch-mode commands.

## License

PlayServ SDK is available under the [MIT License](LICENSE.md). Third-party
components are listed in [Third Party Notices](Third%20Party%20Notices.md).
Runtime schema presence can be checked explicitly with `PlayServData.CheckSchemaAsync(types, ct)`. A table missing from the caller-visible catalogue may be ACL-hidden; this does not validate field compatibility or modify schema.
The optional Game Server companion now supports explicit typed inbound uplink RPC;
see its `RpcRegistry` guide and preserved dedicated-server sample. Client RPC APIs
and room creation remain independent and unchanged.
