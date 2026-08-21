# PlayServ Unity SDK

PlayServ is a modular multiplayer SDK for Unity. The core package provides
runtime connection management, data subscriptions, typed events, RPC, HTTP cloud functions,
read-only catalog/storefront and public platform-status access, spawning,
and the built-in HTTP/WebSocket transport. Analytics, identity providers,
native transports, WebRTC, Pulse, schema tooling, and Dedicated Server runtime
operations are optional companion packages.

## Requirements

- Unity 2021.3 or newer.
- A PlayServ game ID and runtime credential.
- `com.unity.nuget.newtonsoft-json` 3.2.2. Unity Package Manager installs this
  dependency automatically for UPM installations.

## Install

In Unity, open `Window` -> `Package Manager`, choose `Add package from git URL`,
and enter your repository URL with a tag or commit:

```text
https://<git-host>/<organization>/<repository>.git#<tag-or-commit>
```

For a package registry, add `com.playserv.sdk` to the project's
`Packages/manifest.json`.

## Companion packages

Optional PlayServ features are distributed as separate packages. With the Git
repository, install core first and add only the required packages:

```json
{
  "dependencies": {
    "com.playserv.sdk": "git@github.com:playserv1/playserv-unity-sdk.git#<tag-or-commit>",
    "com.playserv.apple-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.apple-signin#<tag-or-commit>",
    "com.playserv.google-signin": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.google-signin#<tag-or-commit>",
    "com.playserv.facebook-login": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.facebook-login#<tag-or-commit>",
    "com.playserv.epic-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.epic-auth#<tag-or-commit>",
    "com.playserv.steam-auth": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.steam-auth#<tag-or-commit>",
    "com.playserv.webrtc": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.webrtc#<tag-or-commit>",
    "com.playserv.analytics": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.analytics#<tag-or-commit>",
    "com.playserv.pulse": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.pulse#<tag-or-commit>",
    "com.playserv.debug-terminal": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.debug-terminal#<tag-or-commit>",
    "com.playserv.transports-native": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.transports-native#<tag-or-commit>",
    "com.playserv.game-server": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.game-server#<tag-or-commit>",
    "com.playserv.schema-tool": "git@github.com:playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/com.playserv.schema-tool#<tag-or-commit>"
  }
}
```

Only add the companion packages used by the game. Unity package dependencies
cannot contain Git URLs, so private Git installations must declare core and
each companion package directly in the project manifest. Use the same tag or
commit for core and every companion package.

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

Open `Tools` -> `PlayServ` -> `Settings`, select an environment and configure the
game ID, user ID, game version, and runtime credential.

```csharp
using Playserv.Wrapper;

PlayServ.Config(new PlayServSettings
{
    ClientToken = "pk_...",
    GameId = "your-game-id",
    UserId = "player-id",
    GameVersion = "1.0.0",
    BackendServerAddress = "wss://your-playserv-endpoint/ws"
});

bool connected = await PlayServ.Connect();
```

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
`LaunchServerAsync(functionSlug, region)`. A successful result means the
deployment was accepted; the room becomes available only after the server
self-registers.

Find, Join, and Launch operational failures use
`PlayServMatchmakingException`, which includes the operation, function slug,
and normalized `UnifiedError`. Caller cancellation and invalid API arguments
keep their existing exception behavior.

The Unity facade intentionally excludes room heartbeat, close, listing, and
reservation consumption endpoints because they require `sk_*`. Use the
PlayServ C# server SDK for that game-server lifecycle.

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
